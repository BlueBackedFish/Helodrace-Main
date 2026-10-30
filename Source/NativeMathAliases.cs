// Scalar arithmetic has no Unity type boundary. Bind it directly to the 1.7
// native implementation; vectors/resources are migrated separately by owner.
global using Mathf = Waylith.Math.Mathf;
