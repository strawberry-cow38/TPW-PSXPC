p='pcsx_rearmed/plugins/gpulib/gpu.c'
s=open(p).read()

old = """    if (gpu->frameskip.active && gpu->frameskip.allow)
      pos += do_cmd_list_skip(gpu, data + pos, count - pos,
               cycles_sum, cycles_last, &cmd);
    else if (gpu_async_enabled(gpu)) {
      pos += gpu_async_do_cmd_list(gpu, data + pos, count - pos,
               cycles_sum, cycles_last, &cmd, &vram_dirty);
    }
    else {
      tpw_scan_list(gpu, data + pos, count - pos);
"""
new = """    /* TPW: the scan MUST sit on all three paths. It was on the plain renderer
     * path only, so anything submitted while frameskip was active or with the
     * async GPU enabled (this core builds with -DUSE_ASYNC_GPU) was never
     * looked at -- an instrument that is blind on two of three branches and
     * silent about it. */
    if (gpu->frameskip.active && gpu->frameskip.allow) {
      tpw_scan_list(gpu, data + pos, count - pos);
      tpw_path_used[0]++;
      pos += do_cmd_list_skip(gpu, data + pos, count - pos,
               cycles_sum, cycles_last, &cmd);
    }
    else if (gpu_async_enabled(gpu)) {
      tpw_scan_list(gpu, data + pos, count - pos);
      tpw_path_used[1]++;
      pos += gpu_async_do_cmd_list(gpu, data + pos, count - pos,
               cycles_sum, cycles_last, &cmd, &vram_dirty);
    }
    else {
      tpw_path_used[2]++;
      tpw_scan_list(gpu, data + pos, count - pos);
"""
assert s.count(old)==1, 'anchor %d' % s.count(old)
s=s.replace(old,new,1)

# counters + an atexit report so the coverage is visible, not assumed
anchor='static void tpw_note_copy'
assert s.count(anchor)==1
s=s.replace(anchor, '''/* which of the three command-list paths actually carried work */
unsigned long tpw_path_used[3] = {0,0,0};

static void tpw_note_copy''', 1)
open(p,'w').write(s)
print('patched')
