@echo off
rem Syntax check of every libil2cpp source the bench patches touch. usage: check4.bat <il2cpp root>
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul
set ROOT=%~1
cl /nologo /Zs /std:c++17 /Zc:__cplusplus /EHsc /MP /W0 /DNDEBUG /DIL2CPP_MONO_DEBUGGER_DISABLED /DBASELIB_INLINE_NAMESPACE=il2cpp_baselib /DRUNTIME_IL2CPP=1 /DGC_NOT_DLL ^
 /I"%ROOT%\libil2cpp" /I"%ROOT%\libil2cpp\pch" /I"%ROOT%\external\baselib\Include" /I"%ROOT%\external\baselib\Platforms\Windows\Include" /I"%ROOT%\external\bdwgc\include" /I"%ROOT%\external" ^
 "%ROOT%\libil2cpp\vm\Class.cpp" "%ROOT%\libil2cpp\vm\ClassInlines.cpp" "%ROOT%\libil2cpp\vm\Object.cpp" "%ROOT%\libil2cpp\metadata\ArrayMetadata.cpp" "%ROOT%\libil2cpp\codegen\il2cpp-codegen.cpp" ^
 "%ROOT%\libil2cpp\vm\GenericClass.cpp" "%ROOT%\libil2cpp\metadata\GenericMethod.cpp" "%ROOT%\libil2cpp\metadata\GenericMetadata.cpp" "%ROOT%\libil2cpp\il2cpp-api.cpp" "%ROOT%\libil2cpp\vm\MetadataAlloc.cpp" "%ROOT%\libil2cpp\metadata\CustomAttributeDataReader.cpp"
echo exit %ERRORLEVEL%
