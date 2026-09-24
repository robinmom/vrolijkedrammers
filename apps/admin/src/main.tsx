import '@fontsource/inter/400.css';
import '@fontsource/inter/600.css';
import '@fontsource/poppins/600.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles.css';
import { applyTheme } from './theme';

applyTheme();

const root = document.getElementById('root');
if (!root) {
  throw new Error('Element #root ontbreekt in index.html');
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
