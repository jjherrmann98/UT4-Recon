# Custom collision donor fixture

`rectangular-prism-v1` is retained evidence for the isolated donor workflow. Its
source T3D is a 240 x 240 x 100 brush, cooked with UT4 Editor CL 3525360/API CL
3525109. The certification manifest pins the package, exact four-export
BlockingVolume closure, geometry, collision payload, dependencies, and supported
placement transforms.

The focused regression transplants this closure into cooked Example_Map, which
does not supply a suitable donor actor. Repeated transplants are byte-identical;
only the persistent Level actor array and four new closure exports may change.
