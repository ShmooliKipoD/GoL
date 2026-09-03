// The simulation references MonoGame.Extended for its ECS, which drags in
// Microsoft.Xna.Framework - and with it a SECOND Vector2. The core must use the
// BCL one: it is the type the whole sim is written against, and the one
// GoL.Render explicitly converts from at the drawing boundary.
//
// Aliasing it globally means an ambiguity can never be resolved the wrong way by
// accident. Any file that genuinely needs the XNA type must name it in full.
global using Vector2 = System.Numerics.Vector2;
