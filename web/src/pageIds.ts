export const PAGE_IDS = ['control', 'planning', 'bookings', 'drivers', 'vehicles', 'engine', 'rules', 'audit'] as const;

export type Page = (typeof PAGE_IDS)[number];

export const isPage = (name: string): name is Page => (PAGE_IDS as readonly string[]).includes(name);
