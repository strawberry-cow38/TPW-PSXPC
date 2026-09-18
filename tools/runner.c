/* Minimal headless libretro frontend: boots a PSX disc, saves frames as PPM,
 * and dumps main RAM on a schedule. No X, no GL, no GUI.
 *
 * Frames are saved sparsely (FRAME_EVERY) because a PSX run at 60fps would
 * otherwise write gigabytes. RAM dumps are what the palettes come out of. */
#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdarg.h>
#include <dlfcn.h>
#include "libretro.h"

static void *core;
static char g_sysdir[512], g_outdir[512];
static unsigned g_fmt = RETRO_PIXEL_FORMAT_0RGB1555;
static unsigned long g_frame = 0;
static int FRAME_EVERY = 60, RUN_FRAMES = 3600;
static int g_dump_every = 600;   /* a TPW game day is 198 frames; 600 is too coarse to see a rollover */

/* scripted input: {from_frame, to_frame, retro_button_id} */
struct press { unsigned long a, b; unsigned id; int pct; };
static struct press SCRIPT[8192];
static int nscript = 0;

static void logcb(enum retro_log_level lvl, const char *fmt, ...) {
    (void)lvl; va_list ap; va_start(ap, fmt);
    fprintf(stderr, "[core] "); vfprintf(stderr, fmt, ap); va_end(ap);
}

static int g_trace = 0;
static bool env_cb(unsigned cmd, void *data) {
    if (g_trace) fprintf(stderr, "[env] cmd=%u\n", cmd);
    switch (cmd) {
    case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
    case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
        *(const char **)data = g_sysdir; return true;
    case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
        g_fmt = *(const enum retro_pixel_format *)data;
        fprintf(stderr, "[fe] pixel format = %u\n", g_fmt); return true;
    case RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
        ((struct retro_log_callback *)data)->log = logcb; return true;
    case RETRO_ENVIRONMENT_GET_CAN_DUPE:
        *(bool *)data = true; return true;
    case RETRO_ENVIRONMENT_GET_VARIABLE: {
        struct retro_variable *v = (struct retro_variable *)data;
        /* Answer only the options that affect REPRODUCIBILITY. The dynarec
         * compiler thread and the threaded renderer both introduce timing
         * variation: identical 12,000-frame runs otherwise diverge by a whole
         * guest arrival (measured FAIL/FAIL/PASS on the same input). Everything
         * else stays at the core default. */
        if (v && v->key) {
            if (!strcmp(v->key, "pcsx_rearmed_drc_thread")) { v->value = "disabled"; return true; }
            if (!strcmp(v->key, "pcsx_rearmed_gpu_thread_rendering")) { v->value = "disabled"; return true; }
        }
        if (v) v->value = NULL;          /* not set -> core keeps its default */
        return false; }
    case RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
        *(bool *)data = false; return true;
    case RETRO_ENVIRONMENT_SET_VARIABLES:
    case RETRO_ENVIRONMENT_SET_CORE_OPTIONS:
    case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2:
    case RETRO_ENVIRONMENT_SET_CONTROLLER_INFO:
    case RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS:
    case RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL:
    case RETRO_ENVIRONMENT_SET_GEOMETRY:
    case RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO:
        return true;
    case RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION:
        *(unsigned *)data = 0; return true;   /* force the simple options path */
    default: return false;
    }
}

static void save_ppm(const void *data, unsigned w, unsigned h, size_t pitch) {
    char path[600];
    snprintf(path, sizeof path, "%s/frame_%06lu.ppm", g_outdir, g_frame);
    FILE *f = fopen(path, "wb"); if (!f) return;
    fprintf(f, "P6\n%u %u\n255\n", w, h);
    for (unsigned y = 0; y < h; y++) {
        const uint8_t *row = (const uint8_t *)data + y * pitch;
        for (unsigned x = 0; x < w; x++) {
            uint8_t r, g, b;
            if (g_fmt == RETRO_PIXEL_FORMAT_XRGB8888) {
                uint32_t p = ((const uint32_t *)row)[x];
                r = p >> 16; g = p >> 8; b = p;
            } else {
                uint16_t p = ((const uint16_t *)row)[x];
                if (g_fmt == RETRO_PIXEL_FORMAT_RGB565) {
                    r = ((p >> 11) & 31) << 3; g = ((p >> 5) & 63) << 2; b = (p & 31) << 3;
                } else { /* 0RGB1555 */
                    r = ((p >> 10) & 31) << 3; g = ((p >> 5) & 31) << 3; b = (p & 31) << 3;
                }
            }
            fputc(r, f); fputc(g, f); fputc(b, f);
        }
    }
    fclose(f);
}

static unsigned long g_next_save = 0;
static void video_cb(const void *data, unsigned w, unsigned h, size_t pitch) {
    /* The core passes NULL for a duplicated frame. Requiring an exact frame
     * number therefore drops most captures; arm a flag and save the next
     * frame that actually carries pixels. */
    if (!data) return;
    if (g_frame >= g_next_save) {
        save_ppm(data, w, h, pitch);
        g_next_save = g_frame + FRAME_EVERY;
    }
}
static void audio_cb(int16_t l, int16_t r) { (void)l; (void)r; }
static size_t audio_batch_cb(const int16_t *d, size_t n) { (void)d; return n; }
static void input_poll_cb(void) {}
/* Analog ids are encoded into the same SCRIPT table above the digital range:
 * 0x100 | (index<<4) | (axis<<1) | sign. TPW's on-screen pointer does not move
 * on the d-pad -- the d-pad pans the camera -- so a purely digital pad can
 * reach a menu and then never press anything on it. */
#define AN_BASE 0x100
static int16_t input_state_cb(unsigned port, unsigned dev, unsigned idx, unsigned id) {
    if (port != 0) return 0;
    if (dev == RETRO_DEVICE_ANALOG) {
        int want_neg = 0, v = 0;
        for (int i = 0; i < nscript; i++) {
            unsigned e = SCRIPT[i].id;
            if (e < AN_BASE) continue;
            if (g_frame < SCRIPT[i].a || g_frame > SCRIPT[i].b) continue;
            if (((e >> 4) & 0xF) != idx) continue;
            if (((e >> 1) & 0x7) != id) continue;
            want_neg = e & 1;
            { int pct = SCRIPT[i].pct ? SCRIPT[i].pct : 100;
              int mag = 32767 * pct / 100;
              v = want_neg ? -mag : mag; }
        }
        return (int16_t)v;
    }
    (void)dev; (void)idx;
    for (int i = 0; i < nscript; i++)
        if (g_frame >= SCRIPT[i].a && g_frame <= SCRIPT[i].b && id == SCRIPT[i].id)
            return 1;
    return 0;
}

/* libretro joypad ids are SNES-style, NOT PlayStation-style:
 *   B = bottom face = CROSS (confirm)   A = right face = CIRCLE
 *   Y = left face  = SQUARE             X = top face   = TRIANGLE (cancel)
 * Getting this backwards is why a menu can look frozen for hours. */
static unsigned btn_id(const char *n) {
    if (!strcasecmp(n,"cross")||!strcasecmp(n,"b"))     return RETRO_DEVICE_ID_JOYPAD_B;
    if (!strcasecmp(n,"circle")||!strcasecmp(n,"a"))    return RETRO_DEVICE_ID_JOYPAD_A;
    if (!strcasecmp(n,"square")||!strcasecmp(n,"y"))    return RETRO_DEVICE_ID_JOYPAD_Y;
    if (!strcasecmp(n,"triangle")||!strcasecmp(n,"x"))  return RETRO_DEVICE_ID_JOYPAD_X;
    /* left stick */
    if (!strcasecmp(n,"lsleft"))  return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_LEFT<<4)  | (RETRO_DEVICE_ID_ANALOG_X<<1) | 1;
    if (!strcasecmp(n,"lsright")) return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_LEFT<<4)  | (RETRO_DEVICE_ID_ANALOG_X<<1);
    if (!strcasecmp(n,"lsup"))    return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_LEFT<<4)  | (RETRO_DEVICE_ID_ANALOG_Y<<1) | 1;
    if (!strcasecmp(n,"lsdown"))  return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_LEFT<<4)  | (RETRO_DEVICE_ID_ANALOG_Y<<1);
    /* right stick */
    if (!strcasecmp(n,"rsleft"))  return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_RIGHT<<4) | (RETRO_DEVICE_ID_ANALOG_X<<1) | 1;
    if (!strcasecmp(n,"rsright")) return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_RIGHT<<4) | (RETRO_DEVICE_ID_ANALOG_X<<1);
    if (!strcasecmp(n,"rsup"))    return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_RIGHT<<4) | (RETRO_DEVICE_ID_ANALOG_Y<<1) | 1;
    if (!strcasecmp(n,"rsdown"))  return AN_BASE | (RETRO_DEVICE_INDEX_ANALOG_RIGHT<<4) | (RETRO_DEVICE_ID_ANALOG_Y<<1);
    if (!strcasecmp(n,"l1")) return RETRO_DEVICE_ID_JOYPAD_L;
    if (!strcasecmp(n,"r1")) return RETRO_DEVICE_ID_JOYPAD_R;
    if (!strcasecmp(n,"l2")) return RETRO_DEVICE_ID_JOYPAD_L2;
    if (!strcasecmp(n,"r2")) return RETRO_DEVICE_ID_JOYPAD_R2;
    if (!strcasecmp(n,"l3")) return RETRO_DEVICE_ID_JOYPAD_L3;
    if (!strcasecmp(n,"r3")) return RETRO_DEVICE_ID_JOYPAD_R3;
    if (!strcasecmp(n,"start"))  return RETRO_DEVICE_ID_JOYPAD_START;
    if (!strcasecmp(n,"select")) return RETRO_DEVICE_ID_JOYPAD_SELECT;
    if (!strcasecmp(n,"up"))     return RETRO_DEVICE_ID_JOYPAD_UP;
    if (!strcasecmp(n,"down"))   return RETRO_DEVICE_ID_JOYPAD_DOWN;
    if (!strcasecmp(n,"left"))   return RETRO_DEVICE_ID_JOYPAD_LEFT;
    if (!strcasecmp(n,"right"))  return RETRO_DEVICE_ID_JOYPAD_RIGHT;
    return 0xffff;
}

/* script file lines: "<start_frame> <end_frame> <button>"; '#' comments.
 * A line "repeat <period> <ondur> <button>" presses that button every period. */
static void load_script(const char *path, int total) {
    FILE *f = fopen(path, "r"); if (!f) { fprintf(stderr,"[fe] no script %s\n",path); return; }
    char line[256];
    while (fgets(line,sizeof line,f) && nscript < 8000) {
        char b[64]; unsigned long a1,a2;
        if (line[0]=='#'||line[0]=='\n') continue;
        if (sscanf(line,"repeat %lu %lu %63s",&a1,&a2,b)==3) {
            unsigned id=btn_id(b); if(id==0xffff) continue;
            for (unsigned long t=300; t<(unsigned long)total && nscript<60; t+=a1)
                SCRIPT[nscript++] = (struct press){t, t+a2, id, 100};
        } else if (sscanf(line,"%lu %lu %63s",&a1,&a2,b)==3) {
            /* "lsdown:25" = 25%% deflection; a digital press is always 100%% */
            int pct = 100; char *c = strchr(b, ':');
            if (c) { *c = 0; pct = atoi(c+1); if (pct < 1 || pct > 100) pct = 100; }
            unsigned id=btn_id(b); if(id==0xffff) continue;
            SCRIPT[nscript++] = (struct press){a1,a2,id,pct};
        }
    }
    fclose(f);
    fprintf(stderr,"[fe] loaded %d script entries from %s\n", nscript, path);
}

typedef void (*fn_setenv)(retro_environment_t);
typedef void (*fn_setvid)(retro_video_refresh_t);
typedef void (*fn_setaud)(retro_audio_sample_t);
typedef void (*fn_setaudb)(retro_audio_sample_batch_t);
typedef void (*fn_setpoll)(retro_input_poll_t);
typedef void (*fn_setstate)(retro_input_state_t);
typedef void (*fn_void)(void);
typedef bool (*fn_load)(const struct retro_game_info *);
typedef void *(*fn_memdata)(unsigned);
typedef size_t (*fn_memsize)(unsigned);
typedef void (*fn_avinfo)(struct retro_system_av_info *);
typedef void (*fn_setport)(unsigned, unsigned);
typedef size_t (*fn_serialize_size)(void);
typedef bool (*fn_serialize)(void *, size_t);
typedef bool (*fn_unserialize)(const void *, size_t);
#define SYM(T,N) T N = (T)dlsym(core, #N); if(!N){fprintf(stderr,"missing %s\n",#N);return 1;}

int main(int argc, char **argv) {
    if (argc < 4) { fprintf(stderr, "usage: runner <core.so> <game> <outdir> [frames] [frame_every]\n"); return 2; }
    snprintf(g_sysdir, sizeof g_sysdir, "%s", getenv("SYSDIR") ? getenv("SYSDIR") : ".");
    snprintf(g_outdir, sizeof g_outdir, "%s", argv[3]);
    g_trace = getenv("TRACE") ? 1 : 0;
    if (getenv("DUMP_EVERY")) g_dump_every = atoi(getenv("DUMP_EVERY"));
    if (argc > 4) RUN_FRAMES = atoi(argv[4]);
    if (argc > 5) FRAME_EVERY = atoi(argv[5]);

    load_script(getenv("INSCRIPT") ? getenv("INSCRIPT") : "input.txt", RUN_FRAMES);

    core = dlopen(argv[1], RTLD_LAZY);
    if (!core) { fprintf(stderr, "dlopen: %s\n", dlerror()); return 1; }

    SYM(fn_setenv, retro_set_environment)
    SYM(fn_setvid, retro_set_video_refresh)
    SYM(fn_setaud, retro_set_audio_sample)
    SYM(fn_setaudb, retro_set_audio_sample_batch)
    SYM(fn_setpoll, retro_set_input_poll)
    SYM(fn_setstate, retro_set_input_state)
    SYM(fn_void, retro_init)
    SYM(fn_load, retro_load_game)
    SYM(fn_void, retro_run)
    SYM(fn_memdata, retro_get_memory_data)
    SYM(fn_memsize, retro_get_memory_size)
    SYM(fn_avinfo, retro_get_system_av_info)
    SYM(fn_serialize_size, retro_serialize_size)
    SYM(fn_serialize, retro_serialize)
    SYM(fn_unserialize, retro_unserialize)
    SYM(fn_setport, retro_set_controller_port_device)

    retro_set_environment(env_cb);
    retro_set_video_refresh(video_cb);
    retro_set_audio_sample(audio_cb);
    retro_set_audio_sample_batch(audio_batch_cb);
    retro_set_input_poll(input_poll_cb);
    retro_set_input_state(input_state_cb);
    retro_init();

    #define RETRO_DEVICE_PSE_DUALSHOCK RETRO_DEVICE_SUBCLASS(RETRO_DEVICE_ANALOG, 1)
    struct retro_game_info gi; memset(&gi, 0, sizeof gi);
    gi.path = argv[2];
    fprintf(stderr, "[fe] calling retro_load_game(%s)\n", gi.path);
    if (!retro_load_game(&gi)) { fprintf(stderr, "[fe] retro_load_game FAILED\n"); return 1; }
    fprintf(stderr, "[fe] retro_load_game returned OK\n");
    /* Pad type must be set HERE. Before retro_load_game the pad subsystem does
     * not exist yet (segfault); after a state restore, padChanged/padReset runs
     * against a half-restored state (segfault). This is the only safe window. */
    if (getenv("PAD_DUALSHOCK")) {
        retro_set_controller_port_device(0, RETRO_DEVICE_PSE_DUALSHOCK);
        fprintf(stderr, "[fe] pad type = DualShock (%d)\n", RETRO_DEVICE_PSE_DUALSHOCK);
    }
    struct retro_system_av_info av; retro_get_system_av_info(&av);
    fprintf(stderr, "[fe] loaded. %ux%u @ %.2f fps\n",
            av.geometry.base_width, av.geometry.base_height, av.timing.fps);

    const char *loadpath = getenv("LOADSTATE");
    if (loadpath) {
        FILE *f = fopen(loadpath, "rb");
        if (!f) fprintf(stderr, "[fe] LOADSTATE %s: cannot open\n", loadpath);
        else {
            size_t need = retro_serialize_size();
            void *buf = malloc(need);
            size_t got = fread(buf, 1, need, f);
            fclose(f);
            /* A state from a different core build will not load. Say so loudly
             * rather than silently continuing from the boot screen, which looks
             * identical to a fresh run and would quietly invalidate the result. */
            if (got != need)
                fprintf(stderr, "[fe] LOADSTATE size %zu, core wants %zu -- REFUSING\n", got, need);
            else if (!retro_unserialize(buf, need))
                fprintf(stderr, "[fe] LOADSTATE retro_unserialize REFUSED it\n");
            else
                fprintf(stderr, "[fe] restored state from %s (%zu bytes)\n", loadpath, need);
            free(buf);
        }
    }

    void *ram = retro_get_memory_data(RETRO_MEMORY_SYSTEM_RAM);
    size_t ramsz = retro_get_memory_size(RETRO_MEMORY_SYSTEM_RAM);
    /* POKE=0xADDR:VALUE -- write a 32-bit word into console RAM after restore.
     * Lets a candidate address be tested by consequence: change it, look at the
     * screen. The address the HUD follows is the one the game actually reads. */
    #define MAXPOKE 8
    static unsigned long pk_addr[MAXPOKE]; static long pk_val[MAXPOKE];
    static int npoke = 0, poke_hold = 0;
    {
        /* POKE=ADDR:VAL[,ADDR:VAL...]  POKE_HOLD=1 re-applies every frame,
         * which is mandatory for any word the game rewrites each tick. */
        const char *pk = getenv("POKE");
        poke_hold = getenv("POKE_HOLD") ? 1 : 0;
        if (pk && ram) {
            char buf[512]; snprintf(buf, sizeof buf, "%s", pk);
            for (char *tok = strtok(buf, ","); tok && npoke < MAXPOKE; tok = strtok(NULL, ",")) {
                unsigned long addr; long val;
                if (sscanf(tok, "%lx:%ld", &addr, &val) == 2 && addr >= 0x80000000
                    && (addr - 0x80000000) + 4 <= ramsz) {
                    *(int32_t *)((char *)ram + (addr - 0x80000000)) = (int32_t)val;
                    pk_addr[npoke] = addr; pk_val[npoke] = val; npoke++;
                    fprintf(stderr, "[fe] poked 0x%08lX = %ld%s\n", addr, val,
                            poke_hold ? " (held)" : "");
                } else fprintf(stderr, "[fe] POKE '%s' rejected\n", tok);
            }
        }
    }

    /* tinyclaw: SPU register file, exposed by our core patch under a private id.
     * Voice n pitch = regs[((n<<4)|4)>>1]; rate Hz = pitch/0x1000 * 44100.
     * Ears can settle one sample's rate; only this shows that voices DIFFER. */
    #define TPW_MEMORY_SPU_REGS 0x1000
    void *spur = retro_get_memory_data(TPW_MEMORY_SPU_REGS);
    size_t spursz = retro_get_memory_size(TPW_MEMORY_SPU_REGS);
    fprintf(stderr, "[fe] SPU regs %p size %zu\n", spur, spursz);
    void *vram = retro_get_memory_data(RETRO_MEMORY_VIDEO_RAM);
    size_t vramsz = retro_get_memory_size(RETRO_MEMORY_VIDEO_RAM);
    fprintf(stderr, "[fe] VRAM %p size %zu\n", vram, vramsz);

    for (g_frame = 0; g_frame < (unsigned long)RUN_FRAMES; g_frame++) {
        if (poke_hold && ram)
            for (int k = 0; k < npoke; k++)
                *(int32_t *)((char *)ram + (pk_addr[k] - 0x80000000)) = (int32_t)pk_val[k];
        retro_run();
        if (g_frame % g_dump_every == 0 && g_frame) {
            char p[600]; FILE *f;
            if (ram && ramsz) {
                snprintf(p, sizeof p, "%s/ram_%06lu.bin", g_outdir, g_frame);
                f = fopen(p, "wb"); if (f) { fwrite(ram, 1, ramsz, f); fclose(f); }
            }
            if (vram && vramsz) {
                snprintf(p, sizeof p, "%s/vram_%06lu.bin", g_outdir, g_frame);
                f = fopen(p, "wb"); if (f) { fwrite(vram, 1, vramsz, f); fclose(f); }
            }
            if (spur && spursz) {
                snprintf(p, sizeof p, "%s/spu_%06lu.bin", g_outdir, g_frame);
                f = fopen(p, "wb"); if (f) { fwrite(spur, 1, spursz, f); fclose(f); }
            }
            fprintf(stderr, "[fe] frame %lu: dumped RAM+VRAM+SPU\n", g_frame);
        }
    }
    const char *savepath = getenv("SAVESTATE");
    if (savepath) {
        size_t need = retro_serialize_size();
        void *buf = malloc(need);
        if (retro_serialize(buf, need)) {
            FILE *f = fopen(savepath, "wb");
            if (f) { fwrite(buf, 1, need, f); fclose(f);
                     fprintf(stderr, "[fe] saved state -> %s (%zu bytes)\n", savepath, need); }
        } else fprintf(stderr, "[fe] retro_serialize FAILED\n");
        free(buf);
    }
    fprintf(stderr, "[fe] done at frame %lu\n", g_frame);
    return 0;
}
