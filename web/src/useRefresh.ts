import { useEffect, useRef, useState } from 'react';
import { createRefresh } from './refresh';

/** Not `scroll`: the page scrolls by itself when its content changes, and a refresh would pass for a person. */
const ACTIVITY = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const;

/** Loads periodically while somebody is looking. Returns whether it is paused because nobody is. */
export function useRefresh(load: () => void): boolean {
  const [paused, setPaused] = useState(false);
  const latestLoad = useRef(load);
  latestLoad.current = load;

  useEffect(() => {
    const refresh = createRefresh({
      load: () => latestLoad.current(),
      onPaused: setPaused,
      isHidden: () => document.hidden,
      listen: ({ onActivity, onVisibility }) => {
        // Some browsers send one when what is under a still pointer changes.
        const onMove = (event: PointerEvent) => {
          if (event.movementX !== 0 || event.movementY !== 0) onActivity();
        };
        const options = { capture: true, passive: true };
        ACTIVITY.forEach(type => addEventListener(type, onActivity, options));
        addEventListener('pointermove', onMove, options);
        document.addEventListener('visibilitychange', onVisibility);
        return () => {
          ACTIVITY.forEach(type => removeEventListener(type, onActivity, options));
          removeEventListener('pointermove', onMove, options);
          document.removeEventListener('visibilitychange', onVisibility);
        };
      },
    });
    return refresh.stop;
  }, []);

  return paused;
}
