# Security policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's **Report a vulnerability** feature for this
repository. Do not open a public issue with exploit steps, proof-of-concept code or a sample file that exposes the
problem.

If private vulnerability reporting is unavailable, open a minimal issue asking for a private contact route. Leave out
technical details until a maintainer provides one.

Include, when possible:

- the affected Kumo version or commit and Windows version/architecture;
- the impact and steps needed to reproduce it;
- a minimal, sanitized sample or proof of concept, shared only through the private report.

Avoid sending files with personal or confidential data. Do not test against another person's files or machine.

## Scope

Reports are especially useful for issues involving image or PDF parsing, file paths and output handling, unintended
data loss or disclosure, update verification, or privilege boundaries. Kumo is designed to process files locally;
its optional update checks contact GitHub but should not transmit file contents or paths.

There is no published response-time guarantee. Maintainers will review valid reports privately and coordinate any fix
and disclosure with the reporter.
