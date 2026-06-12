using UnityEngine;

namespace Sim.Audio {
    /// <summary>
    /// Synthèse procédurale d'un pas (mono, 44.1 kHz) — 100 % DSP, aucun asset.
    /// Modèle : un « corps » d'impact (sinus basse fréquence à décroissance rapide) + un
    /// transient de bruit filtré (passe-bas one-pole), plus selon la surface des résonances
    /// amorties (bois) ou des grains de bruit (gravier). Chaque variation est déterministe via
    /// un seed → variété infinie sans répétition. Baké en WAV par FootstepBaker (éditeur), mais
    /// la classe reste runtime-pure (réutilisable à la volée si besoin).
    /// </summary>
    public static class ProceduralFootsteps {
        public const int SampleRate = 44100;

        public struct Surface {
            public string name;
            public float duration;     // s
            public float bodyFreq;     // Hz (impact)
            public float bodyDecay;    // s
            public float bodyGain;
            public float noiseCutoff;  // Hz (passe-bas one-pole sur le bruit)
            public float noiseDecay;   // s
            public float noiseGain;
            public float res1Freq, res1Decay, res1Gain; // résonance 1 (bois)
            public float res2Freq, res2Decay, res2Gain; // résonance 2 (bois)
            public int   grainCount;   // grains de bruit (gravier)
            public float grainGain;
        }

        /// <summary>Les 5 surfaces par défaut (correspondent aux SfxId Footstep*).</summary>
        public static Surface[] DefaultSurfaces() => new[] {
            new Surface { name = "Default", duration = 0.20f, bodyFreq = 110f, bodyDecay = 0.050f, bodyGain = 0.80f,
                          noiseCutoff = 2500f, noiseDecay = 0.060f, noiseGain = 0.50f },
            new Surface { name = "Wood",    duration = 0.22f, bodyFreq = 120f, bodyDecay = 0.050f, bodyGain = 0.75f,
                          noiseCutoff = 3000f, noiseDecay = 0.050f, noiseGain = 0.45f,
                          res1Freq = 320f, res1Decay = 0.12f, res1Gain = 0.25f,
                          res2Freq = 520f, res2Decay = 0.09f, res2Gain = 0.15f },
            new Surface { name = "Tile",    duration = 0.18f, bodyFreq = 140f, bodyDecay = 0.035f, bodyGain = 0.60f,
                          noiseCutoff = 6000f, noiseDecay = 0.040f, noiseGain = 0.70f,
                          res1Freq = 1800f, res1Decay = 0.05f, res1Gain = 0.20f },
            new Surface { name = "Carpet",  duration = 0.20f, bodyFreq = 95f,  bodyDecay = 0.060f, bodyGain = 0.70f,
                          noiseCutoff = 900f,  noiseDecay = 0.080f, noiseGain = 0.35f },
            new Surface { name = "Outdoor", duration = 0.24f, bodyFreq = 100f, bodyDecay = 0.050f, bodyGain = 0.60f,
                          noiseCutoff = 3500f, noiseDecay = 0.100f, noiseGain = 0.50f,
                          grainCount = 14, grainGain = 0.50f },
        };

        /// <summary>Génère un pas (échantillons mono [-1,1]) pour une surface et un seed.</summary>
        public static float[] Generate(in Surface s, int seed) {
            var rng = new System.Random(seed);
            float dt = 1f / SampleRate;
            int n = Mathf.Max(1, (int)(s.duration * SampleRate));
            var buf = new float[n];

            // Légère variation par pas (pitch/decay) pour casser la répétition.
            float fBody     = s.bodyFreq  * (0.92f + 0.16f * (float)rng.NextDouble());
            float bodyDecay = s.bodyDecay * (0.85f + 0.30f * (float)rng.NextDouble());
            float nzDecay   = s.noiseDecay * (0.85f + 0.30f * (float)rng.NextDouble());

            float a = 1f - Mathf.Exp(-2f * Mathf.PI * s.noiseCutoff * dt); // coeff passe-bas one-pole
            float lp = 0f;
            float p1 = 0f, p2 = 0f;

            for (int i = 0; i < n; i++) {
                float t = i * dt;

                float fGlide = fBody * (1f - 0.25f * (t / s.duration)); // léger glissando descendant
                float body = Mathf.Sin(2f * Mathf.PI * fGlide * t) * Mathf.Exp(-t / bodyDecay) * s.bodyGain;

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += a * (white - lp);
                float noise = lp * Mathf.Exp(-t / nzDecay) * s.noiseGain;

                float val = body + noise;

                if (s.res1Gain > 0f) { p1 += 2f * Mathf.PI * s.res1Freq * dt; val += Mathf.Sin(p1) * Mathf.Exp(-t / s.res1Decay) * s.res1Gain; }
                if (s.res2Gain > 0f) { p2 += 2f * Mathf.PI * s.res2Freq * dt; val += Mathf.Sin(p2) * Mathf.Exp(-t / s.res2Decay) * s.res2Gain; }

                buf[i] = val;
            }

            // Grains (gravier) : petits bursts de bruit dans les ~80 premières ms.
            if (s.grainCount > 0) {
                int window = Mathf.Max(1, (int)(0.08f * SampleRate));
                for (int g = 0; g < s.grainCount; g++) {
                    int start = rng.Next(0, window);
                    int glen = Mathf.Max(1, (int)((0.002f + 0.004f * (float)rng.NextDouble()) * SampleRate));
                    float gg = s.grainGain * (0.5f + 0.5f * (float)rng.NextDouble());
                    for (int k = 0; k < glen && start + k < n; k++) {
                        float env = 1f - (k / (float)glen);
                        buf[start + k] += (float)(rng.NextDouble() * 2.0 - 1.0) * env * gg;
                    }
                }
            }

            // Anti-clic (fondu d'entrée ~2 ms) + normalisation + saturation douce.
            int fade = Mathf.Max(1, (int)(0.002f * SampleRate));
            for (int i = 0; i < fade && i < n; i++) buf[i] *= i / (float)fade;

            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            if (peak > 1e-4f) {
                float norm = 0.9f / peak;
                for (int i = 0; i < n; i++) {
                    float v = buf[i] * norm;
                    buf[i] = Mathf.Clamp(v - 0.15f * v * v * v, -1f, 1f); // soft clip
                }
            }

            return buf;
        }
    }
}
