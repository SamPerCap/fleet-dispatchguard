"""Fails on the medium and high alerts of an OWASP ZAP JSON report (SPEC 0091), except the accepted ones.

Usage: zap_gate.py <report.json>

The exit code of zap-baseline.py is not enough: it does not tell a medium alert from an informational one.
Prints the Markdown to stdout; the workflow appends it to $GITHUB_STEP_SUMMARY.
"""

import json
import sys

MEDIUM = 2

# By the reference of the alert and not by its plugin: plugin 10055 is every weakness a CSP can have.
ACCEPTED = {
    "10055-6": "style-src unsafe-inline: the dialog library inserts a style (SPEC 0091 R3); scripts are never inline",
}


def main(path: str) -> int:
    with open(path, encoding="utf-8") as report:
        sites = json.load(report).get("site", [])
    alerts = [alert for site in sites for alert in site.get("alerts", [])]
    if not alerts:
        # There is always some informational alert: a report without any is a target that did not answer.
        print("### OWASP ZAP\n\n❌ The report has no alerts of any kind: nothing was scanned.")
        return 1

    print("### OWASP ZAP\n\n| Risk | Alert | Where | |\n|---|---|---|---|")
    failed = False
    for alert in sorted(alerts, key=lambda each: -int(each["riskcode"])):
        is_serious = int(alert["riskcode"]) >= MEDIUM
        accepted = ACCEPTED.get(alert["alertRef"])
        failed |= is_serious and not accepted
        verdict = (f"accepted: {accepted}" if accepted else "❌") if is_serious else ""
        where = ", ".join(sorted({instance["uri"] for instance in alert.get("instances", [])})[:3])
        print(f"| {alert['riskdesc']} | {alert['name']} ({alert['alertRef']}) | {where} | {verdict} |")
    return int(failed)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
