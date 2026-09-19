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
        /// each, subs 0-9. ⚠ Which of the ten the menu uses has NOT been established, so nothing here
        /// picks one.</summary>
        public const int MenuSubMeshFirst = 0, MenuSubMeshLast = 9;

        /// <summary>Faces of <see cref="SubMesh"/> that are the flag, and the palette they carry.</summary>
        public const int FlagFaces = 192;
        public const ushort FlagClut = 0x40e0;
    }
}
