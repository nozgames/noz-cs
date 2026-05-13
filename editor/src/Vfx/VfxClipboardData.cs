//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public sealed class VfxClipboardData
{
    public VfxDocEmitter[] Emitters { get; }
    public VfxDocParticle[] Particles { get; }

    public VfxClipboardData(IReadOnlyList<VfxDocEmitter> emitters, IReadOnlyList<VfxDocParticle> particles)
    {
        Emitters = new VfxDocEmitter[emitters.Count];
        for (var i = 0; i < emitters.Count; i++)
            Emitters[i] = VfxDocEmitter.Clone(emitters[i]);

        Particles = new VfxDocParticle[particles.Count];
        for (var i = 0; i < particles.Count; i++)
            Particles[i] = VfxDocParticle.Clone(particles[i]);
    }
}
