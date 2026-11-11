# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.

from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

from validate_results import validate_results


class ValidateResultsTests(unittest.TestCase):
    def check_results(self, assembly, valid):
        with TemporaryDirectory() as directory:
            path = Path(directory) / "testResults.xml"
            path.write_text(f"<assemblies>{assembly}</assemblies>", encoding="utf-8")
            if valid:
                validate_results(path)
            else:
                with self.assertRaises(ValueError):
                    validate_results(path)

    def test_passes_and_skips(self):
        self.check_results(
            '<assembly total="2" passed="1" failed="0" skipped="1" errors="0">'
            '<collection><test result="Pass"/><test result="Skip"/></collection>'
            '</assembly>',
            valid=True,
        )

    def test_failure(self):
        self.check_results(
            '<assembly total="2" passed="1" failed="1" skipped="0" errors="0">'
            '<collection><test result="Pass"/><test result="Fail"/></collection>'
            '</assembly>',
            valid=False,
        )

    def test_reporter_error_without_failed_test_row(self):
        self.check_results(
            '<assembly total="2" passed="1" failed="1" skipped="0" errors="1">'
            '<errors><error type="fatal"/></errors>'
            '<collection><test result="Pass"/></collection></assembly>',
            valid=False,
        )

    def test_incomplete_results(self):
        self.check_results(
            '<assembly total="2" passed="2" failed="0" skipped="0">'
            '<collection><test result="Pass"/></collection></assembly>',
            valid=False,
        )

    def test_empty_results(self):
        self.check_results("", valid=False)
        self.check_results(
            '<assembly total="0" passed="0" failed="0" skipped="0"/>',
            valid=False,
        )


if __name__ == "__main__":
    unittest.main()
