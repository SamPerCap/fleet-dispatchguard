import { useSyncExternalStore } from 'react';
import { language, subscribe, type Language } from './language';

export const useLanguage = (): Language => useSyncExternalStore(subscribe, language, language);
