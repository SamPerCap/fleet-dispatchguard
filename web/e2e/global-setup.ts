import { execFileSync } from 'node:child_process';
import { iso, nextMonday } from './week';

// Seeds the demo week through the running API; idempotent.
export default function globalSetup() {
  execFileSync('dotnet', ['run', '--project', '../tools/SampleData', '--', '--api', 'http://localhost:5171', '--week', iso(nextMonday())], {
    stdio: 'inherit',
  });
}
