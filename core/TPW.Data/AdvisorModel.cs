namespace TPW.Data
{
    /// <summary>The advisor -- the little character who holds the flag on the language screen and
    /// appears again on the main menu.
    ///
    /// ⭐ IDENTIFIED BY WHAT THE CONSOLE DREW, not by looking for a character-shaped mesh. Entry 83's
    /// sub-meshes use eight texture palettes, and SIX of them are palettes the console's display list
    /// uses on the language screen (0x07b9, 0x07ba, 0x07bb, 0x09b8, 0x0938, 0x08f8). A coincidence of
    /// six palettes is not available.
    ///
    /// ⭐ AND THE FLAG IS PART OF HIM. 192 of sub-mesh 10's 319 faces carry CLUT 0x40e0 over u 1..149,
    /// v 88..173 -- exactly the 12x8 cloth measured off the display list -- leaving 88 vertices for his
    /// body and the pole. The wave is his BONES (29 of them), not a cloth simulation.
    ///
    /// ⚠ This corrects an earlier conclusion of mine that the cloth was generated in code. It was not;
    /// my search fingerprinted whole meshes and so could not see a mesh that is part of a bigger one,
    /// and I read its silence as an answer. Found by cow tools.</summary>
    public static class AdvisorModel
    {
        /// <summary>FOLIO entry holding the advisor's meshes.</summary>
        public const int Entry = 83;

        /// <summary>The language screen's advisor: 205 vertices, 319 faces, 29 bones, flag included.</summary>
        public const int SubMesh = 10;

        /// <summary>The menu's advisor, without the pole: ten sub-meshes of 72 vertices and 103 faces
        /// each, subs 0-9 -- ten animation CLIPS of one model, not ten models (same geometry and bone
        /// count throughout, different track lengths).
        ///
        /// ⚠ WHICH CLIP THE MENU PLAYS IS STILL NOT ESTABLISHED, and the measurement says it is none of
        /// them on its own. The menu advisor's pose repeats exactly every 84 frames -- confirmed twice,
        /// at frames 114 and 198 against frame 30, point for point, and no lag under 40 repeats at all.
        /// At the game's animation rate that is 103.4 units, and entry 83's clip cycles are 24, 24, 24,
        /// 32, 33, 42, 52, 102, 102, 102. The nearest, 102, predicts 82.9 frames.
        ///
        /// ⚠ 1.1 frames is too big to be rounding: the same arithmetic predicts the LANGUAGE advisor's
        /// period as 104.0 against 104 measured. So the menu is doing something this model does not
        /// describe -- clips in sequence, a different clock, or a clip I have not found. Two clips of
        /// cycle 52 would come to 84.5 frames, which is the closest fit available and is exactly the
        /// sort of coincidence that should not be written down as a finding.
        ///
        /// ⭐ The MODEL is settled even though the clip is not: a disc-wide search for sub-meshes using
        /// the menu advisor's six palettes returns entry 83 and nothing else.</summary>
        public const int MenuSubMeshFirst = 0, MenuSubMeshLast = 9;

        /// <summary>Faces of <see cref="SubMesh"/> that are the flag, and the palette they carry.</summary>
        public const int FlagFaces = 192;
        public const ushort FlagClut = 0x40e0;
    }
}
