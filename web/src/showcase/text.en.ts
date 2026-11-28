import type { ShowcaseText } from './text';

export const en: ShowcaseText = {
  tagline: 'Legal allocation of drivers and buses',
  cover: {
    title: 'It never saves an illegal assignment. Try to make it.',
    problem:
      "The hard part isn't finding someone who is free, but someone who can legally drive: breaks, rest periods and limits on driving time (EC 561/2006).\nFleet DispatchGuard checks it before saving and, when a rule is broken, says which one and why.",
    tryIt: 'Try it',
    howItIsBuilt: "How it's built",
    about: 'About',
    technologies: 'Technologies',
    notes: (wait, measuredOn) => [
      'No sign-up.',
      'Sample data, rebuilt every night.',
      `The first visit wakes the database up: ${wait} in the last measurement (${measuredOn}).`,
    ],
  },
  evidence: {
    title: 'Evidence, not adjectives',
    intro: 'Each figure was measured again for this page and names the test or the tool that reproduces it.',
    items: {
      bursts: {
        figure: '60 of 480',
        claim: 'Clashing assignments accepted: exactly one in each burst.',
        method:
          '60 bursts of 8 simultaneous requests that give one driver eight services at the same hour, split over two API instances on one real PostgreSQL 17. The other 420 are rejected as an overlap.',
      },
      oracle: {
        figure: '0 of 10,102',
        claim: 'Disagreements with an independent reading of Article 6 (driving times).',
        method:
          'Random four-week calendars around both clock changes of 2026 and an ordinary week. For every service offered, the engine must name the same articles as an oracle written from the text of Art. 6, which shares no code with it.',
      },
      speed: {
        figure: '4.4 µs',
        claim: 'To check one driver against EC 561/2006 with two weeks of history.',
        method:
          'BenchmarkDotNet, Release build, .NET 10, on an AMD Ryzen 7 7800X3D. A new driver takes 1.3 µs. It is the check run for every candidate before proposing or assigning.',
      },
    },
    measuredOn: date => `Measured in ${date}`,
    reproduce: 'Reproduce it',
    readTheCode: 'Read the code',
  },
  flow: {
    title: 'How an assignment is decided',
    description:
      'An assignment request locks the service, the driver and the bus. Then, in this order: 1, the driver is active and not on leave during the service; 2, EC 561/2006, including the later services already assigned to the driver; 3, the bus is active, has enough seats and is not in the workshop; 4, neither the driver nor the bus has an overlapping service. At the first no, nothing is saved. Only step 2 can be forced, by a person who gives a name and a reason. If every step passes, the assignment is saved. The attempt is recorded either way.',
    request: { name: 'Assignment request', detail: 'Locks service, driver and bus' },
    steps: {
      driver: { name: '1. Driver available', detail: 'Active, no leave in its hours' },
      compliance: { name: '2. EC 561/2006', detail: 'Breaks, rest and later trips' },
      vehicle: { name: '3. Bus fit', detail: 'Active, seats, not in workshop' },
      overlap: { name: '4. No overlap', detail: 'Driver first, then the bus' },
    },
    forcible: 'Forcible: name and reason',
    rejected: ['No:', 'rejected'],
    saved: { name: 'Saved', detail: 'The attempt is recorded either way' },
    caption:
      'At the first “no” nothing is saved, and the attempt is recorded with its reason. Only step 2 can be overridden, by a person who gives a name and a reason: the engine validates all the same and the trip is marked “Forced”. An unavailable driver, an unfit bus or an overlap is never forced, and neither is a recommendation.',
  },
  about: {
    back: 'Cover',
    title: 'About',
    contact: 'Contact',
    story: [
      'I built Fleet DispatchGuard with generative AI (Claude Code) to practise working with the tools the industry is adopting. The AI writes code; I own the engineering. I write the specification and approve the plan before any code exists, the legal rules are tested first with their edge cases, and nothing is closed without a clean build and green tests against a real PostgreSQL. The design follows Clean Architecture, domain-driven design (DDD) and SOLID. The problem it solves: assigning a driver and a bus without breaking Regulation (EC) No 561/2006.',
    ],
    architecture: {
      title: 'Architecture, patterns and technology',
      intro: 'The decisions that shape the code, each with its reason. More, also with their reasons, are on the engineering page.',
      points: [
        {
          title: 'Clean Architecture, four layers',
          why: 'Domain, application, infrastructure and API, with dependencies pointing inwards: the rules of the Regulation know neither the database nor HTTP, so they are tested with nothing around them.',
        },
        {
          title: 'Domain-driven design',
          why: 'The service is the aggregate that accepts or refuses its driver and bus; time windows and the result of each check are value objects, and the legal figures live in one single file.',
        },
        {
          title: 'Results as values, exceptions at one point',
          why: 'Each check returns a value that says what was broken and why; only the service that assigns turns a “no” into a domain exception, so the order of the checks is in one place.',
        },
        {
          title: 'Repository and unit of work',
          why: 'Repositories never save: one unit of work confirms everything or nothing, inside the locks of the service, the driver and the bus.',
        },
        {
          title: 'PostgreSQL locks, always in the same order',
          why: 'Transaction-level advisory locks per service, driver and bus, taken by id: two assignments can never cross, also with several instances.',
        },
        {
          title: 'One bilingual catalogue',
          why: 'Every text a person reads, in the API and the web, comes from a catalogue in English and Spanish; the code decides with codes, never with texts.',
        },
      ],
      technologies: [
        { group: 'Backend', items: ['.NET 10 · Minimal APIs', 'Entity Framework Core', 'PostgreSQL 17'] },
        { group: 'Tests', items: ['xUnit', 'Testcontainers', 'BenchmarkDotNet', 'Vitest', 'Playwright'] },
        { group: 'Web', items: ['React 18 · TypeScript', 'Vite', 'Tailwind CSS', 'ESLint'] },
        { group: 'Delivery', items: ['One Docker image for API and web', 'Vercel', 'Neon'] },
      ],
    },
    role: {
      title: 'My role and the AI\'s',
      intro: 'The decisions below are mine: I chose them, I can defend them, and each one is checked by something other than my word.',
      decisions: [
        {
          title: 'Reject before saving',
          why: 'An illegal assignment is never stored and then corrected. Only an EC 561/2006 breach can be forced, by a person who gives a name and a reason; the attempt is recorded. An unfit bus, an overlap or an unavailable driver can never be forced.',
        },
        {
          title: 'An independent oracle for Article 6',
          why: 'The engine is checked against a second reading of Article 6 written from the text, sharing no code with it: 0 disagreements in 10,102 random cases.',
        },
        {
          title: 'Locks per service, driver and bus',
          why: 'Two dispatchers can click at the same moment. Advisory locks taken in a fixed order make exactly one clashing assignment win per burst: 60 of 480, across two API instances.',
        },
        {
          title: 'A demo that sleeps',
          why: 'The cover asks for no session and wakes no database; the database wakes only when someone goes in, and the data are rebuilt every night. The public repository is exported by allowlist, with checks before anything leaves.',
        },
      ],
      assistant: {
        title: 'What the AI did',
        why: 'Claude Code wrote most of the code, tests and documentation from my specifications and the plans I approved. I reviewed every change, confirmed every regulatory rule and run the release myself.',
      },
      limits: {
        title: 'Limits',
        why: 'Tests show that the cases they check work, not that there are no errors. The reading of the Regulation is mine and is not legal advice. The figures were measured on one machine on the date they show, and what could not be verified is written in the closing notes of each specification.',
      },
    },
    code: {
      title: 'The code',
      pending:
        'The repository will be published soon, with all rights reserved. Until then, what this site states can be seen in the demo or is explained with its reason.',
      link: 'Read the code',
    },
  },
  engineering: {
    back: 'Cover',
    title: "How it's built",
    intro:
      'The decisions behind it, how the work is done and what went wrong. What is stated here can be seen in the demo or checked in the public code.',
    sections: [
      {
        id: 'arquitectura',
        title: 'Architecture',
        intro:
          'A layered .NET 10 backend (domain, application, infrastructure and API) and a React + TypeScript web. Dependencies point inwards: the domain knows neither the database nor HTTP.',
        points: [
          {
            title: 'The law lives in the domain, without a database',
            why: 'The rules of EC 561/2006 are pure calculation over driving periods. That way they are tested with nothing around them, and the legal figures are in a single file.',
          },
          {
            title: 'One image serves the API and the web',
            why: 'Same origin: no CORS, and the session cookie always stays inside. It is also what is started whole before publishing.',
          },
          {
            title: 'The database sleeps',
            why: 'The demo has few visits and a minimal cost. That is why this cover asks it nothing, the web waits for it to wake up and refreshes stop if nobody is looking.',
          },
        ],
      },
      {
        id: 'decisiones',
        title: 'Decisions and why',
        intro: 'Every important decision has a record (ADR) with what was discarded. These are the ones that weigh most.',
        points: [
          {
            title: 'It is validated before saving, never afterwards',
            why: 'Finding an infringement with the service already under way is too late. The order is fixed (driver available, regulation, vehicle, overlap) and at the first failure nothing is saved.',
          },
          {
            title: 'Forcing is possible, but only one rule and leaving a trace',
            why: 'Sometimes a person has to decide. They can skip an infringement of EC 561/2006 with a reason and a name; the engine validates all the same and the trip is marked. An overlap or an unfit vehicle is never forced.',
          },
          {
            title: 'Recommending uses the same classes as assigning',
            why: 'Two engines would end up disagreeing. The proposal is simulated on an in-memory copy with the same code, and when it is applied every line is validated again.',
          },
          {
            title: 'Simultaneous assignments: a lock per service, driver and vehicle',
            why: 'Reading, deciding and saving have to be one thing. The locks are PostgreSQL ones and are always taken in the same order, so they cannot cross and they hold with several instances.',
          },
          {
            title: 'The week is counted in Madrid time and in UTC',
            why: 'The Regulation does not say in which clock the week changes. Both must be met, and the strictest figure is shown.',
          },
        ],
      },
      {
        id: 'seguridad',
        title: 'Security',
        intro: 'No sign-in, on purpose: it is an open demo. That makes the rest matter more.',
        points: [
          {
            title: 'Signed anonymous session',
            why: 'It identifies a browser, not a person. Without it nothing is read or written, and writes also carry a header of their own against forged requests.',
          },
          {
            title: 'Strict content policy',
            why: 'The web only loads what is its own: neither inline scripts nor other domains. It can be seen in the headers of this very page.',
          },
          {
            title: 'Least privilege',
            why: 'The container has no shell or package manager and does not run as an administrator. The application user in the database only touches rows.',
          },
          {
            title: 'Request limits per session and per address',
            why: 'A heavy calculation repeated over and over cannot leave the demo without service for everybody else.',
          },
        ],
      },
      {
        id: 'proceso',
        title: 'Process, as it is',
        intro:
          'This project is programmed with an AI assistant (Claude Code). A person decides what is done, approves every plan and tests the result; the rest are checks that do not depend on anybody’s good faith.',
        points: [
          {
            title: '1. SPEC',
            why: 'A short document: what, why, rules and how it will be known to be right. It includes what is left out.',
          },
          {
            title: '2. Plan reviewed by a person',
            why: 'It is approved, corrected or cut down before any code is written. The texts of this cover were approved like that.',
          },
          {
            title: '3. Tests first',
            why: 'For every legal rule, the edge cases are written before the rule. The person confirms the rules: the assistant does not “correct” them.',
          },
          {
            title: '4. Gates',
            why: 'A change is not closed unless it builds without warnings and with the tests green. What could not be verified is written in the closing notes.',
          },
        ],
      },
      {
        id: 'pruebas',
        title: 'How it is tested and published',
        intro: 'Tests with doubles are not enough: that was learnt the hard way.',
        points: [
          {
            title: 'A real PostgreSQL',
            why: 'Persistence, concurrency and permissions are tested against the same version of the database, in a container.',
          },
          {
            title: 'An independent oracle',
            why: 'The weekly rules are compared with another calculation written from the legal text, with no code of the engine.',
          },
          {
            title: 'Publishing is one command',
            why: 'It builds, runs the .NET and web tests, starts the image as it will be deployed, migrates if needed, publishes and checks what was published. It stops at the first failure.',
          },
          {
            title: 'The demo goes through the same door as everybody',
            why: 'The data are loaded every night through the public API: if an assignment were illegal, the load would fail.',
          },
        ],
      },
      {
        id: 'errores',
        title: 'What went wrong',
        intro: 'Three real failures and what changed afterwards.',
        points: [
          {
            title: 'All green and it did not work',
            why: 'In the first SPECs the tests used doubles. Almost all data access threw “not implemented” and nobody saw it. Since then, whatever touches the database or HTTP is tested for real.',
          },
          {
            title: 'Two people at once',
            why: 'With the usual tests green, every request that overlapped another was accepted. Now reading, deciding and saving happen with the resources locked, and there are tests that force the clash at the exact point.',
          },
          {
            title: 'A test that did not test the rule',
            why: 'The example of the service that crosses Monday passed without the rule, and the oracle used the same time-zone library as the engine. Now every part of a limit rule is broken on purpose to see that some test fails.',
          },
        ],
      },
    ],
  },
  scene: {
    messages: [
      'Loading the database…',
      'Setting up the bases…',
      'Building the schedules…',
      'Inventing excuses for absences…',
    ],
    allLoaded: 'All loaded!',
    waking: (wait, measuredOn) =>
      `The database switches off when nobody uses it and is now starting. The last time it took ${wait} (${measuredOn}); you will go in as soon as it is ready.`,
    unavailable: 'The demo is not available now',
    timedOut: 'Two minutes have gone by and the database has not answered. Try again in a while.',
  },
  pageFailed: 'This page could not be shown.',
  appFailed: 'The application could not be loaded.',
  tryAgain: 'Try again',
};
