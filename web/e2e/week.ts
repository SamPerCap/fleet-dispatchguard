/** The services are in the future, so they accept leave and candidates. */
export function nextMonday(now = new Date()): Date {
  const date = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  date.setDate(date.getDate() + ((8 - date.getDay()) % 7 || 7));
  return date;
}
export const iso = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
