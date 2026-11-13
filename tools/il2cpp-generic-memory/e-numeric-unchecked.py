"""Writes Assets/Tests/GenericMemory/Scenario/NumericKernelsUnchecked.cs: NumericKernels.cs with IL2CPP's null checks
and array bounds checks turned off for the whole class ([Il2CppSetOption]). The two files have to be the same kernels,
so the second is made from the first and never edited by hand.

usage: e-numeric-unchecked.py          writes the file
       e-numeric-unchecked.py --check  says whether the file is what this would write (exit 1 when not)
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SCENARIO = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Tests", "GenericMemory", "Scenario"))

SUMMARY_OLD = """	/// <summary>
	/// The kernels of <see cref="NumericBench"/> as plain C#: what IL2CPP makes C++ of, with its null checks and its
	/// array bounds checks. NumericKernelsUnchecked.cs is this file with the checks turned off, written by
	/// tools/il2cpp-generic-memory/e-numeric-unchecked.py: change a kernel here and run that script.
	/// </summary>
	public static class NumericKernels
"""
SUMMARY_NEW = """	/// <summary>
	/// The kernels of <see cref="NumericKernels"/> once more, with IL2CPP told to leave the null checks and the array
	/// bounds checks out of the C++ it generates for this class. Written by
	/// tools/il2cpp-generic-memory/e-numeric-unchecked.py from NumericKernels.cs: do not edit it by hand.
	/// </summary>
	[Il2CppSetOption(Option.NullChecks, false)]
	[Il2CppSetOption(Option.ArrayBoundsChecks, false)]
	public static class NumericKernelsUnchecked
"""
USING_OLD = "using System.Runtime.CompilerServices;\n"
USING_NEW = "using System.Runtime.CompilerServices;\nusing Unity.IL2CPP.CompilerServices;\n"


def main():
    with open(os.path.join(SCENARIO, "NumericKernels.cs"), encoding="utf-8", newline="") as file:
        text = file.read()
    newline = "\r\n" if "\r\n" in text else "\n"
    for old, new in ((SUMMARY_OLD, SUMMARY_NEW), (USING_OLD, USING_NEW)):
        old, new = old.replace("\n", newline), new.replace("\n", newline)
        if text.count(old) != 1:
            sys.exit("NumericKernels.cs: expected one of %r, found %d" % (old[:50], text.count(old)))
        text = text.replace(old, new)
    target = os.path.join(SCENARIO, "NumericKernelsUnchecked.cs")
    if "--check" in sys.argv[1:]:
        with open(target, encoding="utf-8", newline="") as file:
            same = file.read() == text
        print("NumericKernelsUnchecked.cs is " + ("what NumericKernels.cs makes" if same else "NOT what NumericKernels.cs makes"))
        sys.exit(0 if same else 1)
    with open(target, "w", encoding="utf-8", newline="") as file:
        file.write(text)
    print("written: " + target)


main()
