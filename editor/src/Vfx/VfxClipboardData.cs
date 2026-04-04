//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class VfxClipboardData
{
    public struct EmitterData
    {
        public string Name;
        public VfxEmitterDef Def;
        public string ParticleRef;
    }

    public struct ParticleData
    {
        public string Name;
        public VfxParticleDef Def;
        public DocumentRef<SpriteDocument> SpriteRef;
    }

    public EmitterData[] Emitters { get; }
    public ParticleData[] Particles { get; }

    public VfxClipboardData(IReadOnlyList<VfxDocEmitter> emitters, IReadOnlyList<VfxDocParticle> particles)
    {
        Emitters = new EmitterData[emitters.Count];
        for (var i = 0; i < emitters.Count; i++)
        {
            Emitters[i] = new EmitterData
            {
                Name = emitters[i].Name,
                Def = emitters[i].Def,
                ParticleRef = emitters[i].ParticleRef,
            };
        }

        Particles = new ParticleData[particles.Count];
        for (var i = 0; i < particles.Count; i++)
        {
            Particles[i] = new ParticleData
            {
                Name = particles[i].Name,
                Def = particles[i].Def,
                SpriteRef = particles[i].SpriteRef,
            };
        }
    }
}
