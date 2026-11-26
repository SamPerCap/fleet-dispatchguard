// The tests read the Spanish texts, the ones there were before the catalogue: that is how they prove they did not change. The
// English ones have tests of their own.
import { catalogueReady } from './i18n/t';
import { setLanguage } from './language';
import { showcaseReady } from './showcase/showcaseText';

await Promise.all([catalogueReady(), showcaseReady()]);
await setLanguage('es');
