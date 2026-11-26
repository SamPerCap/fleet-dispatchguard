import { read, write } from './storage';

// Declared by whoever types it, and kept in this browser: there are no users. What the API recognises is the session (SPEC 0084).
const KEY = 'dispatchguard.assignedBy';
export function assignedBy(): string {
  return (read(KEY) ?? '').trim();
}
export function setAssignedBy(name: string): void {
  write(KEY, name.trim());
}
