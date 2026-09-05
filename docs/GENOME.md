# The genome

A creature's heritable design: its body traits, which latent attributes its lineage
has acquired, and the topology and weights of its brain.

The headline requirement — *"new attributes will be added over time by birth of new
mutations"* — is what shapes everything here. The set of attributes in the
population must be able to **grow over generations**, not be fixed when the game
was written.

## Shape

```csharp
sealed class Genome {
    float[]              TraitValues;  // by TraitAxis, normalized 0..1
    ulong                UnlockedMask; // one bit per LatentTraitId; only ever grows
    List<NodeGene>       Nodes;        // sorted by Id
    List<ConnGene>       Conns;        // sorted by (To, From)
}
```

**An array and a bitmask, not a dictionary and a hash set.** Mutation picks "some
trait" to perturb and the attribute overlay lists "every trait", and both iterate —
hash iteration order is not guaranteed stable, and a run must be reproducible from
its seed. The array form is also faster, trivially serializable, and turns "list
exactly this creature's attributes" into a mask walk.

Traits are stored **normalized 0..1** and mapped through `Traits.Range(axis)` on
read. So mutation is one clamped Gaussian nudge for every axis with no per-axis step
size to tune, and retuning a range is a data change that cannot fall out of sync
with the mutation code.

## Continuous traits

Every creature has all fourteen. `docs/CONTROLS.md`'s `A` overlay lists them live.

| Axis | Range | What it governs |
|---|---|---|
| `BodyRadius` | 3–14 u | Size. More energy storage, more upkeep |
| `MaxSpeed` | 10–90 u/s | Top forward speed |
| `TurnRate` | 1–6 rad/s | The spec's *agility* |
| `EyeRange` | 30–130 u | How far it sees |
| `EyeHalfFov` | 0.15–1.3 rad | Half the field of view |
| `NoseRadius` | 0–120 u | Pheromone sampling radius |
| `MouthReach` | 0.5–5 u | How far past the body it can bite |
| `MouthArc` | 0.2–1.2 rad | Half the bite arc; it must face its food |
| `DigestGrass` / `DigestFruit` | 0.3–1.0 | Digestive efficiency |
| `Metabolism` | 0.7–1.4x | The fast-living / slow-living axis |
| `MatureAge` | 20–120 s | When it can breed |
| `ReproduceThreshold` | 0.45–0.9 | Energy fraction needed to breed |
| `Hue` | 0–1 | Body colour. Cosmetic **until** `ColorVision` exists |

## Node ids are derived, not allocated

The single most useful decision in the design:

```csharp
Sensor(traitSlot, channel)  = SensorBase   + traitSlot * 32 + channel
Effector(traitSlot, channel)= EffectorBase + traitSlot * 32 + channel
```

A sensor's id is computed from *which trait owns it*, so the same sensor has the
same id in every genome in every run — for free, with no registry to keep in sync
and no counter whose value could leak into results. Unlocking a trait just inserts
the ids that trait declares. Only hidden neurons get counter-allocated ids, and only
within one genome, which is fine because reproduction is asexual.

Slots 0–3 are innate (innate senses, forward vision). Latent traits start at slot 4.

## Latent attributes

Thirteen, each with a metabolic upkeep and the nodes it registers. `Prerequisite`
gives a shallow tech tree with no extra machinery.

**Append new ones to the end of `LatentTraitId`.** Node-id slots are handed out in
catalog build order, so inserting anywhere else renumbers every existing trait's
sensors and effectors.

| Attribute | Needs | Upkeep | Adds |
|---|---|---|---|
| Colour vision | — | 0.010 | 7 sensors: hue per vision bin |
| Rear eye | — | 0.014 | 15 sensors: a second, shorter eye behind |
| Second nostril | — | 0.006 | 1 sensor: a second pheromone channel |
| Scent gradient | Second nostril | 0.012 | 4 sensors: which way a smell strengthens |
| Scent gland | — | 0.008 | 1 effector: lays a trail |
| Second gland | Scent gland | 0.008 | 1 effector: a second pheromone |
| Carnivory | — | 0.020 | 1 sensor, 1 effector; can eat creatures |
| Cellulose gut | — | 0.026 | Digests bramble, which nothing else can eat |
| Toxin resistance | — | 0.018 | 1 sensor; shrugs off blightcap |
| Sprint gland | — | 0.004 | 1 effector: speed burst at steep cost |
| Memory cell | — | 0.009 | 2 self-connected hidden neurons |
| Armour plating | — | 0.030 | Halves bite damage, costs speed |
| Torpor | — | 0.005 | 1 effector: rest cheaply, but barely move and barely see |

**Every attribute costs upkeep.** A free attribute is never selected against, so
populations would accumulate all thirteen and none would signify anything.

Torpor is the only one that *saves* energy, so it carries three costs rather than
one: running cost ×0.35, thrust and turn ×0.15, eye range ×0.5. A lineage that
evolves it survives famine; one that oversleeps gets eaten. Its own upkeep is
charged in full even while the gate is open — an attribute that paid for itself
whenever it was used could never be selected against.

## Mutation

Operators fire in a fixed order, each drawing a known number of values from the
creature's stream — that is what makes a lineage reproducible from its seed.

| Operator | Default rate | Effect |
|---|---|---|
| Perturb every trait | 1.0 | `v += N(0, 0.04)`, clamped |
| Perturb weights | 0.85 | 90% nudge by `N(0, 0.12)`, 10% replace outright |
| Perturb biases | always | `b += N(0, 0.08)` |
| Toggle a connection | 0.02 | Flips one enabled flag |
| Add connection | 0.20 | Between two existing nodes |
| Add neuron | 0.05 | Splits an enabled connection |
| **Unlock an attribute** | **0.004** | The mechanism this project is about |

Rates live in `SimConfig` (so the configuration screen can retune them without a
rebuild); the *strengths* live in `MutationStrength`.

### Why any mutant is guaranteed to work

Evaluability is **structural**, not a validation pass that could itself be wrong:

1. **No mutation ever removes a node.** Unlocks are monotonic.
2. `AddConnection` samples both endpoints from the existing node list.
3. `AddNeuron` splits an existing connection, so both endpoints already exist.
4. Cycles need no handling at all — see evaluation below.

A dangling reference is therefore unconstructible. `GenomeMutationTests` fuzzes
3000 chained mutations at inflated rates and asserts every result still compiles and
produces finite, bounded outputs.

### Why an unlock wires itself in immediately

`Mutator.Unlock` gives every new sensor at least one outgoing connection and every
new effector at least one incoming one. Without that the attribute would cost upkeep
while being invisible to the brain, and selection would remove it before it ever had
a chance to be useful — the classic reason an open-ended trait system quietly does
nothing.

Memory neurons additionally get a self-loop weighted 0.75–0.95. That self-loop *is*
the memory.

`AddNeuron` splits `a→b` into `a→h→b` with the incoming weight at 1 and the outgoing
weight inherited, so the new neuron starts nearly behaviourally neutral — a
structural change that does not immediately destroy what the lineage had learned.

## Brain evaluation

The genome's lists are the storage format; `Brain` is the runtime format, compiled
once at birth into flat arrays that are owned for life and reused on recompile.

**One pass over the previous tick's activations.** Every connection reads its source
from the previous buffer and accumulates into the current one; then biases and the
activation function are applied and the buffers swap.

```
for each connection c:   cur[c.to] += prev[c.from] * c.weight
for each non-sensor n:   cur[n] = softsign(cur[n] + bias[n])
swap(prev, cur)
```

This makes **cycles and self-loops legal by construction**. There is no topological
sort to get wrong, no cycle detection that must stay correct across every mutation
operator, and no tie-breaking rule that could leak into results. The cost is one
tick of propagation delay per layer — tens of milliseconds at 60 Hz, which is
biologically plausible and imperceptible.

Activation is **softsign** (`x / (1 + |x|)`): one divide, no call into libm, so it
is fast and bit-identical across runs without depending on a platform's
transcendental implementation. It saturates to (-1, 1), which is what keeps a
runaway recurrent loop bounded rather than producing infinities.

Connections are accumulated in `(To, From)` order — a key derived purely from node
ids, never from insertion order. Float addition is not associative, so accumulation
order is part of the simulation's semantics: two genomes that reached the same
topology by different mutation paths must evaluate identically.

## Senses and actions

Innate sensors: energy, age, speed, turn rate, mouth contact, mouth contact kind,
bump, ground fertility, pheromone channel 0, and a fixed 1.5 s **oscillator**.

That oscillator is a deliberate gift. Without a free rhythm source, evolving any
gait at all first requires evolving a recurrent oscillator from scratch — a steep
first step that most lineages never take.

Innate effectors: thrust, turn, bite, reproduce.

**The creature has exactly eight actions**, and the readout's vocabulary
(`CreatureAction`) *is* that effector set: Move, Turn, Bite, Reproduce innately,
plus Sprint, Scent A, Scent B and Torpor once the matching attribute is unlocked.

Chasing and fleeing are **not two actions** — they are one signed `Move`, chase
positive and flee negative. Left and right are likewise one signed `Turn`. The brain
is a continuous controller, not a planner: it emits a thrust every tick and never
chooses between named behaviours. Splitting one effector into two named entries
would invent a distinction the controller does not have, and would then need a "is
something in view?" heuristic to sustain it. Reading the sign needs no heuristic and
cannot drift from the real model.

"Chase" and "Flee" are therefore human words for the two signs of a thrust. The
creature is advancing or reversing and has no notion of a pursuer.

The action a creature is *most* doing is the greatest magnitude this tick, ties
broken in a fixed documented order — `Reproduce, Bite, Torpor, Sprint, ScentA,
ScentB, Move, Turn` — discrete events ahead of continuous ones. Nothing above the
deadband is **idle**, which is deliberately not an enum member: idle is the absence
of an action, not one of them.

Vision divides each eye's cone into **7 fixed angular bins** reporting the nearest
hit per bin as (closeness, is-plant, is-creature). Fixed-size regardless of how many
things are in view — which is what makes a variable-topology genome workable — with
occlusion for free, and directly drawable as the `V` overlay.

## Energy

```
upkeep/s = (base + 0.30·mass + 0.0016·(nodes + conns) + Σ attribute upkeep)
         × Metabolism × ageFactor

movement/s = 1.60·mass·(speed/max)² × sprint + 0.22·mass·|turn|/turnRate
```

Movement is **quadratic in speed** so cruising is cheap and flat-out is expensive;
otherwise "always maximum" would dominate. Sprinting multiplies cost by 4.5 for a
1.9x speed gain, so it is a genuine trade-off.

Ageing is a quadratic rise in upkeep past `MatureAge`, not a hard cutoff — creatures
die of no longer being able to pay for themselves, which is both more natural and one
fewer special case.

Reproduction needs all four of: the brain asking, age past `MatureAge`, energy above
`ReproduceThreshold`, and 3 s since the last birth.

## Eating

A bite draws `BiteRate` energy per second from the target, and the eater absorbs
`take × Digestibility(kind, genome)`. Both environments credit it through
`Energy.Gain`, which is the **only** way energy is ever added: it clamps to the
maximum and records `LifetimeIntake` in the same place, so the intake readout cannot
disagree with the energy it reports on. It also credits what was *absorbed* rather
than what was offered, so a full creature biting a plant correctly gains nothing.

**An eaten green is gone.** A stripped plant used to stay in place at zero energy
and refill from its roots, which left an invisible plant a creature could sit on
forever, grazing regrowth for less than its own upkeep. Now the cell is cleared, so
food has to be found again — which is what makes foraging worth evolving. Partial
grazing is still survivable; only a plant eaten to nothing disappears.

Because spread only ever fills a cell beside a living plant, destructible greens
introduced a way for a kind to be eaten to extinction with no path back. A **low-rate
ambient reseed** is the floor against that, gated on `SpreadChance > 0` so it can
never conjure carrion, and on the same soil limits everything else obeys.

A plant that has exhausted the soil under it **dies off**, reusing its own
`MinFertility` as the line. Without that, nothing removes a plant except being
eaten, and vegetation only ever spreads.

### Motor commitment

Effector gates are **Schmitt triggers**, not bare thresholds: a gate opens above 0.5
and stays open until the output falls below 0.2. Without the gap, a brain output
resting near the threshold flips it every tick and the creature flutters its mouth
sixty times a second instead of taking a bite — measured at **8.76 changes of action
per creature-second** in the lab before this, 3.9 after.

`Reproduce` is hysteretic too, which was worth checking rather than assuming: it
feeds `CanReproduce`, so a latched gate could in principle mean breeding every
cooldown forever. Measured on the board it makes no difference (149 against 146 by
tick 12 000), because a birth still has to pass age, cooldown and an energy
threshold, and an open gate creates none of those.

Turning **eases** toward its target the way speed already did. A body cannot reverse
its turn within a sixtieth of a second, and that instant reversal was where the
visible twitch came from.

The mouth **latches onto the plant it started**, kept in `Mind.BiteTarget`, and holds
it while it lasts and stays in reach. `Metabolism.WorthBiting` governs what a mouth
will *pick*, not whether it finishes — a latch that let go at the threshold would
leave a stub behind to regrow, which is the opposite of eating a green out.

All three are properties of a **body**, not of a plan. Nothing here steers a creature
toward food: pursuing what it can see remains something evolution has to discover,
and an unevolved genome will still sit and chew on nothing.

### Density is the lever, not per-plant value

What a creature experiences is how *often* it finds food, not how fat each morsel
is. The Creature Lab had 14 plants in a 340-unit arena and a creature there
essentially never ate — measured intake **0.00/s across every seed tried**, starving
in about two minutes. The board works because it is roughly **seventy times denser**
in plants per unit area: a creature there cannot avoid food. The lab now seeds 240,
putting mean nearest-plant distance inside the eye.

Reach for density before reaching for `MaxEnergy`.

## Known risk

A newly unlocked attribute arrives with random wiring and immediate upkeep, so it is
usually *worse* than the parent and selection removes it before it can be tuned.
This is the classic problem speciation exists to solve, and it is the most likely
reason the headline feature could appear not to work once populations run in Step 4.

Escalation, in order: waive upkeep for a new lineage's first few seconds → raise the
unlock rate → protect recently-unlocked lineages from competition. The `Innovation`
field on `ConnGene` is already reserved so crossover and speciation can be added
without a genome format change.
