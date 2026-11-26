"""Markdown summary of test reports (.trx and JUnit XML) for the GitHub run page.

Usage: test_summary.py [--require-tests] "Suite name=glob" ["Suite name=glob" ...]

Prints the Markdown to stdout; the workflow appends it to $GITHUB_STEP_SUMMARY.
Failing tests do not change the exit code: each suite's own step already fails the job.
"""

import argparse
import glob
import html
import re
import sys
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass, field

TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
MAX_LISTED_FAILURES = 50
MAX_MESSAGE_LENGTH = 300
ERROR_LINE = re.compile(r"^\s*\w*Error: .*$", re.MULTILINE)
TERMINAL_COLOURS = re.compile(r"\x1b\[[0-9;]*m")


@dataclass
class Suite:
    name: str
    passed: int = 0
    skipped: int = 0
    failures: list[tuple[str, str]] = field(default_factory=list)
    # A crashed test host leaves a failed report with no failed test.
    broken_runs: list[str] = field(default_factory=list)

    @property
    def executed(self) -> int:
        return self.passed + len(self.failures)

    @property
    def result(self) -> str:
        if self.failures or self.broken_runs:
            return "❌ failed"
        return "✅ passed" if self.executed else "⚠️ not run"


def first_line(text: str | None) -> str:
    lines = TERMINAL_COLOURS.sub("", text or "").strip().splitlines()
    return lines[0][:MAX_MESSAGE_LENGTH] if lines else ""


def read_trx(root: ElementTree.Element, suite: Suite) -> None:
    failures_before = len(suite.failures)
    # The <Counters> totals are not used: VSTest leaves notExecuted at 0 for skipped tests.
    for test in root.iter(f"{TRX}UnitTestResult"):
        outcome = test.get("outcome", "")
        if outcome == "Passed":
            suite.passed += 1
        elif outcome == "NotExecuted":
            suite.skipped += 1
        else:
            message = test.findtext(f"{TRX}Output/{TRX}ErrorInfo/{TRX}Message") or outcome
            suite.failures.append((test.get("testName", "?"), first_line(message)))

    run = root.find(f"{TRX}ResultSummary")
    if run is not None and run.get("outcome") not in ("Completed", "Passed") and len(suite.failures) == failures_before:
        details = [first_line(text.text) for text in run.iter(f"{TRX}Text")]
        suite.broken_runs.append(next(filter(None, details), run.get("outcome") or "unknown error"))


def junit_message(problem: ElementTree.Element, test_name: str) -> str:
    message = first_line(problem.get("message"))
    if message and test_name not in message:
        return message
    # Playwright puts the test title in the message and the error further down in the text.
    error = ERROR_LINE.search(problem.text or "")
    return first_line(error.group() if error else message or problem.text)


def read_junit(root: ElementTree.Element, suite: Suite) -> None:
    for test in root.iter("testcase"):
        problem = test.find("failure")
        if problem is None:
            problem = test.find("error")
        if problem is not None:
            name = test.get("name", "?")
            suite.failures.append((f"{test.get('classname', '')} › {name}", junit_message(problem, name)))
        elif test.find("skipped") is not None:
            suite.skipped += 1
        else:
            suite.passed += 1


def read_suite(name: str, pattern: str) -> Suite:
    suite = Suite(name)
    for path in sorted(glob.glob(pattern, recursive=True)):
        try:
            root = ElementTree.parse(path).getroot()
        except ElementTree.ParseError as error:
            suite.broken_runs.append(f"{path}: {error}")
            continue
        if root.tag == f"{TRX}TestRun":
            read_trx(root, suite)
        else:
            read_junit(root, suite)
    return suite


def failure_list(suite: Suite) -> list[str]:
    lines = [f"- {html.escape(problem)}" for problem in suite.broken_runs]
    for name, message in suite.failures[:MAX_LISTED_FAILURES]:
        detail = f"<br>{html.escape(message)}" if message else ""
        lines.append(f"- <code>{html.escape(name)}</code>{detail}")
    hidden = len(suite.failures) - MAX_LISTED_FAILURES
    if hidden > 0:
        lines.append(f"- … and {hidden} more (see the attached report)")
    return lines


def markdown(suites: list[Suite]) -> str:
    lines = ["| Suite | Result | Passed | Failed | Skipped |", "| --- | --- | ---: | ---: | ---: |"]
    for suite in suites:
        lines.append(f"| {suite.name} | {suite.result} | {suite.passed} | {len(suite.failures)} | {suite.skipped} |")
    for suite in suites:
        if suite.failures or suite.broken_runs:
            lines += ["", f"#### {suite.name}: what failed", "", *failure_list(suite)]
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    # In a job that is green so far, a suite without tests means a broken filter or report path, not a red test before it.
    parser.add_argument("--require-tests", action="store_true")
    parser.add_argument("suites", nargs="+", metavar="NAME=GLOB")
    arguments = parser.parse_args()

    suites = [read_suite(*suite.split("=", 1)) for suite in arguments.suites]
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdout.write("### Tests\n\n" + markdown(suites))

    without_tests = [suite.name for suite in suites if not suite.executed]
    if arguments.require_tests and without_tests:
        print(f"::error::No test results for: {', '.join(without_tests)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
