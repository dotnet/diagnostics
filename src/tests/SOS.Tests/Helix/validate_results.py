# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

import sys
import xml.etree.ElementTree as ET


def validate_results(path):
    root = ET.parse(path).getroot()
    assemblies = root.findall("assembly")
    if not assemblies:
        raise ValueError(f"No xUnit assembly results found in {path}")

    for assembly in assemblies:
        total = int(assembly.attrib["total"])
        passed = int(assembly.attrib["passed"])
        failed = int(assembly.attrib["failed"])
        skipped = int(assembly.attrib["skipped"])
        errors = int(assembly.get("errors", "0"))
        tests = assembly.findall("./collection/test")
        if (
            passed <= 0
            or failed != 0
            or errors != 0
            or root.findall(".//error")
            or total != passed + failed + skipped
            or total != len(tests)
            or passed != sum(test.get("result") == "Pass" for test in tests)
            or skipped != sum(test.get("result") == "Skip" for test in tests)
        ):
            raise ValueError(
                f"Invalid or failing xUnit results in {path}: "
                f"total={total}, passed={passed}, failed={failed}, "
                f"skipped={skipped}, errors={errors}, test rows={len(tests)}"
            )


if __name__ == "__main__":
    validate_results(sys.argv[1])
