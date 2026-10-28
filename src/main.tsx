import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './tokens.css';
import './app.css';
import 'highlight.js/styles/github.css';

createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
