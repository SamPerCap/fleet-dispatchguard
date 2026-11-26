import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { startLanguage } from './language';
import { Root } from './showcase/Root';
import { showcaseReady } from './showcase/showcaseText';
import './index.css';

// Nothing is painted before the texts of the chosen language are there.
void showcaseReady()
  .then(startLanguage)
  .then(() =>
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <Root />
      </StrictMode>,
    ),
  );
