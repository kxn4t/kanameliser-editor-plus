namespace Kanameliser.EditorPlus
{
    internal class MeshInfoData
    {
        public int Triangles { get; set; }
        public int Materials { get; set; }
        public int Meshes { get; set; }
        public int MaterialSlots { get; set; }
        public int ParticleSystems { get; set; }
        public int ParticleMaterialSlots { get; set; }
        public int TrailLineMaterialSlots { get; set; }
        public bool HasChildObjects { get; set; }

        public void Reset()
        {
            Triangles = 0;
            Materials = 0;
            Meshes = 0;
            MaterialSlots = 0;
            ParticleSystems = 0;
            ParticleMaterialSlots = 0;
            TrailLineMaterialSlots = 0;
            HasChildObjects = false;
        }

        public MeshInfoData Clone()
        {
            return new MeshInfoData
            {
                Triangles = Triangles,
                Materials = Materials,
                Meshes = Meshes,
                MaterialSlots = MaterialSlots,
                ParticleSystems = ParticleSystems,
                ParticleMaterialSlots = ParticleMaterialSlots,
                TrailLineMaterialSlots = TrailLineMaterialSlots,
                HasChildObjects = HasChildObjects
            };
        }
    }
}
