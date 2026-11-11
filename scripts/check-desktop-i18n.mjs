import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const app = path.join(root, 'desktop', 'FullAIon2Companion');
const html = fs.readFileSync(path.join(app, 'ui', 'index.html'), 'utf8');
const dictionary = JSON.parse(fs.readFileSync(path.join(app, 'i18n', 'panel.json'), 'utf8')).en;

function decode(value) {
  return value.replace(/&(#x[0-9a-f]+|#\d+|amp|lt|gt|quot|apos|nbsp);/gi, (_, entity) => {
    if (entity[0] === '#') return String.fromCodePoint(entity[1].toLowerCase() === 'x' ? parseInt(entity.slice(2), 16) : Number(entity.slice(1)));
    return { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: '\u00a0' }[entity.toLowerCase()];
  });
}

const visible = new Set();
const add = value => {
  const text = decode(value).trim();
  if (/[a-záéíóúñü]/i.test(text) && !/^https?:\/\//i.test(text)) visible.add(text);
};
for (const match of html.matchAll(/>([^<>]+)</g)) add(match[1]);
for (const match of html.matchAll(/\b(?:placeholder|title|aria-label|alt)="([^"]+)"/g)) add(match[1]);

// Product names, language autonyms and named visual themes are intentionally unchanged.
const ignored = new Set([
  'FullAIon2Companion', 'AION 2', 'Daeva Companion', 'Discord', 'NCSOFT', 'PvP', 'DPS', 'HPS', 'APS', 'Kinah', 'WebView2',
  'English', 'Français', 'Português (Brasil)', 'Arcade', 'Bushido', 'Cyberpunk', 'Esports', 'Gamer Girl', 'Kawaii',
  'Noir', 'Sakura', 'Steampunk', 'Synthwave', 'Terminal',
]);
const missing = [...visible].filter(text => !dictionary[text] && !ignored.has(text)).sort((a, b) => a.localeCompare(b, 'es'));
const empty = Object.entries(dictionary).filter(([, translation]) => typeof translation !== 'string' || !translation.trim()).map(([text]) => text);
console.log(`${visible.size} textos estáticos; ${missing.length} sin traducción inglesa exacta; ${empty.length} traducciones vacías`);
for (const text of missing) console.log(text);
for (const text of empty) console.log(`Vacía: ${text}`);
if (missing.length || empty.length) process.exitCode = 1;
