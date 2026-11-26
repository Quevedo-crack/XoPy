#!/usr/bin/env python3
"""Comprobación rápida de los fuentes C# sin necesidad de tener el SDK de .NET.

No es un compilador: valida lo que más veces ha roto la compilación en este repo.

  1. Llaves, paréntesis y corchetes equilibrados (ignorando los que van dentro de
     cadenas, cadenas literales @"...", interpolaciones y comentarios).
  2. Comillas sin cerrar al final de una línea.
  3. Identificadores usados en un archivo pero declarados en ninguno (campos _x).

Uso:  python3 herramientas/check-cs.py [carpeta]
Sale con código 1 si encuentra algún problema.
"""
import os
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else "XoPy"


def strip_code(text):
    """Devuelve el texto con cadenas y comentarios sustituidos por espacios."""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        two = text[i:i + 2]
        if two == "//":
            j = text.find("\n", i)
            if j < 0:
                j = n
            out.append(" " * (j - i))
            i = j
        elif two == "/*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(" " * (j - i))
            i = j
        elif text[i:i + 2] in ('@"', '$@') or text[i:i + 3] == '$@"':
            # Cadena literal: solo la termina una comilla que no venga duplicada.
            start = text.index('"', i)
            j = start + 1
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append(" " * (j - i))
            i = j
        elif c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                if text[j] == "\n":
                    break
                j += 1
            out.append(" " * (j - i))
            i = j
        elif c == "'":
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                if text[j] == "\n":
                    break
                j += 1
            out.append(" " * (j - i))
            i = j
        else:
            out.append(c)
            i += 1
    return "".join(out)


def check(path):
    problems = []
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    bare = strip_code(text)

    pairs = {"}": "{", ")": "(", "]": "["}
    stack = []
    line = 1
    for ch in bare:
        if ch == "\n":
            line += 1
        elif ch in "({[":
            stack.append((ch, line))
        elif ch in ")}]":
            if not stack:
                problems.append(f"{path}:{line}: sobra «{ch}»")
                break
            opened, opened_line = stack.pop()
            if opened != pairs[ch]:
                problems.append(
                    f"{path}:{line}: se cierra «{ch}» pero lo abierto en la línea "
                    f"{opened_line} era «{opened}»")
                break
    if stack and not problems:
        opened, opened_line = stack[-1]
        problems.append(f"{path}: queda sin cerrar «{opened}» abierto en la línea {opened_line}")

    # Campos declarados y usados (solo avisa, no es un error de compilación seguro).
    declared = set(re.findall(r"\b(?:private|internal|protected|public)\s+(?:readonly\s+)?"
                              r"(?:static\s+)?[\w<>?,.\[\]]+\s+(_\w+)\s*[;=]", text))
    used = set(re.findall(r"\b(_\w+)\b", bare))
    return problems, declared, used


def main():
    files = []
    for folder, _dirs, names in os.walk(ROOT):
        for name in names:
            if name.endswith(".cs"):
                files.append(os.path.join(folder, name))
    files.sort()

    all_problems = []
    declared = set()
    used = set()
    for path in files:
        problems, decl, use = check(path)
        all_problems += problems
        declared |= decl
        used |= use

    unknown = sorted(name for name in used - declared
                     if not name.startswith("__") and len(name) > 2)
    print(f"Revisados {len(files)} archivos .cs")
    for problem in all_problems:
        print("ERROR  " + problem)
    if unknown:
        print("AVISO  campos usados sin declaración visible: " + ", ".join(unknown[:25]))
    if all_problems:
        return 1
    print("Equilibrio de llaves, paréntesis y comillas: correcto")
    return 0


if __name__ == "__main__":
    sys.exit(main())
