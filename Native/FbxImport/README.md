# FBX import helper

ufbx v0.21.3 from https://github.com/ufbx/ufbx/tree/v0.21.3, unmodified `ufbx.c` and `ufbx.h`; license in LICENSE and the application's embedded THIRD-PARTY-NOTICES.txt.

Build from this directory in an x64 Visual Studio developer command prompt:

```bat
cl /nologo /O2 /MT main.c ufbx.c /Fe:../../Assets/fbx-import.exe
```

The project embeds the resulting static-CRT executable. `main.c` accepts input FBX and output temporary binary paths, parses without loading external assets, and converts the scene to metre units / right-handed Y-up coordinates matching FbxWriter. No runtime Blender or Python dependency. C# verifies helper contents before use, enforces an execution timeout, and validates the imported mesh against the original BINFBX before modifying it.
