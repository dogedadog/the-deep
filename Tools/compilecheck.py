"""
Type-checks the project's C# outside the Unity editor, using Unity's own Roslyn compiler and the
references from the Unity-generated .csproj files. Safe to run while Unity is open (writes only to
a temp folder). Usage:

    python Tools/compilecheck.py            # runtime + editor assemblies
    python Tools/compilecheck.py Footage    # only show diagnostics whose path contains "Footage"

Exit code 0 when there are no errors.
"""
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = r"C:\Program Files\Unity\Hub\Editor\6000.3.22f1\Editor\Data"
CSC = os.path.join(UNITY, "DotNetSdkRoslyn", "csc.dll")


def parse(csproj):
    tree = ET.parse(os.path.join(ROOT, csproj))
    ns = ""
    files, refs, analyzers, defines, lang = [], [], [], "", "9.0"
    for el in tree.iter():
        tag = el.tag.split("}")[-1]
        if tag == "Compile" and el.get("Include"):
            files.append(os.path.join(ROOT, el.get("Include")))
        elif tag == "HintPath" and el.text:
            refs.append(el.text.strip())
        elif tag == "ProjectReference" and el.get("Include"):
            name = os.path.splitext(os.path.basename(el.get("Include")))[0]
            refs.append(("project", name))
        elif tag == "Analyzer" and el.get("Include"):
            analyzers.append(el.get("Include"))
        elif tag == "DefineConstants" and el.text:
            defines = el.text.strip()
        elif tag == "LangVersion" and el.text:
            lang = el.text.strip()
    return files, refs, analyzers, defines, lang


def project_scripts(editor):
    """Every .cs under Assets/_Project (new files Unity hasn't added to the .csproj yet included)."""
    found = []
    for folder, _, names in os.walk(os.path.join(ROOT, "Assets", "_Project")):
        in_editor = os.sep + "Editor" in folder
        for n in names:
            if n.endswith(".cs") and in_editor == editor:
                found.append(os.path.join(folder, n))
    return found


def compile_assembly(csproj, out_dir, built):
    files, refs, analyzers, defines, lang = parse(csproj)
    # Drop deleted files, add new project files the .csproj doesn't know about yet.
    editor = "Editor" in csproj
    known = {os.path.normcase(os.path.abspath(f)) for f in files}
    files = [f for f in files if os.path.exists(f)]
    for f in project_scripts(editor):
        if os.path.normcase(os.path.abspath(f)) not in known:
            files.append(f)
    name = os.path.splitext(csproj)[0]
    out = os.path.join(out_dir, name + ".dll")
    args = ["dotnet", CSC, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-deterministic-",
            "-langversion:" + lang, "-nowarn:0169,0649,0414,0618,0067,1998,8321", "-out:" + out]
    if defines:
        args.append("-define:" + defines)
    for r in refs:
        if isinstance(r, tuple):
            dll = built.get(r[1]) or os.path.join(ROOT, "Library", "ScriptAssemblies", r[1] + ".dll")
            if os.path.exists(dll):
                args.append("-r:" + dll)
        elif os.path.exists(r):
            args.append("-r:" + r)
    for a in analyzers:
        if os.path.exists(a):
            args.append("-analyzer:" + a)
    rsp = os.path.join(out_dir, name + ".rsp")
    with open(rsp, "w", encoding="utf-8") as f:
        for a in args[2:]:
            if a == "-noconfig":
                continue  # only allowed on the command line
            if a.startswith("-") and ":" in a and " " in a:
                key, _, value = a.partition(":")
                a = key + ':"' + value + '"'
            f.write(a + "\n")
        for src in files:
            f.write('"' + src + '"\n')
    result = subprocess.run(["dotnet", CSC, "-noconfig", "@" + rsp], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if os.path.exists(out):
        built[name] = out  # else later assemblies fall back to Unity's last good build
    return result.stdout + result.stderr


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else None
    out_dir = tempfile.mkdtemp(prefix="deepcheck_")
    built = {}
    errors = 0
    for csproj in ["Assembly-CSharp.csproj", "Assembly-CSharp-Editor.csproj"]:
        if not os.path.exists(os.path.join(ROOT, csproj)):
            continue
        text = compile_assembly(csproj, out_dir, built)
        for line in text.splitlines():
            if "error CS" not in line:
                continue
            if only and only.lower() not in line.lower():
                continue
            errors += 1
            print(line.replace(ROOT + os.sep, ""))
    print(f"{errors} error(s)")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()
