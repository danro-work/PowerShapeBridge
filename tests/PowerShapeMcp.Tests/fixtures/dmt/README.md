# Autodesk DMT reader fixtures

Three binary fixtures are taken from Autodesk/PowerShapeAndPowerMillAPI at
commit f0761e50ae833e7647423b8da3ba24bfc23bd93d. source-manifest.jsonl records
original repository paths, byte lengths and SHA256 values from Git LFS pointers.
The actual LFS bytes were fetched and matched, rather than storing pointer text.
The accompanying Autodesk MIT license is retained in AUTODESK-LICENSE.txt.

NormalDmt and TwoBlocks counts/bounds are independently specified by
Delcam.Geometry.Test/GeometricEntities/DMTModelReaderTest.cs at the same commit.
smallModel adds a denser file; its structure is independently decoded during
Framework qualification. All three file flags are 0: units remain unknown despite
the human-readable header saying 'in MM'. They are file parser fixtures, not
protected user models or evidence of current PowerShape export/convention support.
