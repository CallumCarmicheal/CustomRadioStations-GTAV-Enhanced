Enhanced build dependencies
===========================

Required to COMPILE:
  ScriptHookVDotNet2.dll  - from the SAME ScriptHookVDotNet Enhanced release installed in GTA V
  irrKlang.NET4.dll       - x64 .NET 4 assembly used by the original mod

Required at RUNTIME by irrKlang (copy the matching x64 versions from the original mod/runtime):
  irrKlang.dll
  ikpMP3.dll              - when MP3 plugin is used
  ikpFlac.dll             - when FLAC plugin is used

The build script stages any of the runtime DLLs above that are present in this lib directory.
Do not mix x86 and x64 irrKlang files.
