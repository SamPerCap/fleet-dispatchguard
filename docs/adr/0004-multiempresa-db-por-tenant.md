# ADR 0004 — Multi-company: one backend, one database per company, rules as data

- **Status:** accepted (implementation in M2) · **Origin:** SPECs 0018, 0019, 0027–0029

## Context
Each company has its fleet, its drivers and, sometimes, agreements stricter than the law. Data isolation between companies is non-negotiable.

## Decision
A single application (pooled app) with one database per company, chosen per request from the tenant of the authenticated user. The differences in behaviour are **data**: thresholds that can only tighten the law and a catalogue of rule templates, without a DSL.

## Consequences
- The entities do not carry `TenantId`; a filtering mistake cannot mix data from two companies.
- A rules DSL remains a future extension.
