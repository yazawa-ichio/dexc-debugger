using System;

// Runs the steps of the whole-program weaver on a folder of assemblies, outside Unity, so that a step can be tried on
// the assemblies of a build without building. usage: WeaverHost <folder of DLLs, rewritten in place> <step+step...>
static class Program
{
	static int Main(string[] args)
	{
		Console.WriteLine(GenericMemory.Editor.GenericMemoryWeaver.Run(args[0], args[1].Split('+')));
		return 0;
	}
}
