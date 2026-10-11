import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './styles.css';
import './suite.css';
import './focus.css';
import './selection.css';
import './spacing.css';
import './scrollbars.css';
import './dialogDismiss';
createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>);

// Reveal the first invalid field, including fields inside scrolling dialogs.
let revealingInvalidField=false;
document.addEventListener('invalid',event=>{
 const field=event.target;
 if(!(field instanceof HTMLElement))return;
 field.setAttribute('aria-invalid','true');
 if(revealingInvalidField)return;
 revealingInvalidField=true;
 requestAnimationFrame(()=>{field.scrollIntoView({block:'center',inline:'nearest'});field.focus({preventScroll:true});revealingInvalidField=false;});
},true);
document.addEventListener('input',event=>{if(event.target instanceof HTMLElement)event.target.removeAttribute('aria-invalid');},true);
