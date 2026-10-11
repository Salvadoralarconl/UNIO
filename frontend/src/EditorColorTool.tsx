import {useRef,useState} from 'react';
import {Baseline,Highlighter,ChevronDown} from 'lucide-react';
const swatches=[['Yellow','#ffff00'],['Bright green','#00ff00'],['Turquoise','#00ffff'],['Pink','#ff00ff'],['Blue','#0000ff'],['Red','#ff0000'],['Dark blue','#000080'],['Teal','#008080'],['Green','#008000'],['Violet','#800080'],['Dark red','#800000'],['Dark yellow','#808000'],['Gray 50%','#808080'],['Gray 25%','#c0c0c0'],['Black','#000000']];
export default function EditorColorTool({kind,value,active=false,disabled,apply,choose,stop}:{kind:'text'|'highlight';value:string;active?:boolean;disabled:boolean;apply:()=>void;choose:(color:string)=>void;stop?:()=>void}){
 const palette=useRef<HTMLDivElement>(null),arrow=useRef<HTMLButtonElement>(null);const [open,setOpen]=useState(false);const label=kind==='text'?'Text color':'Text highlight';
 function toggle(){if(palette.current?.matches(':popover-open'))palette.current.hidePopover();else{const rect=arrow.current!.getBoundingClientRect();palette.current!.style.left=Math.max(8,Math.min(rect.left-29,window.innerWidth-214))+'px';palette.current!.style.top=Math.min(rect.bottom+6,window.innerHeight-320)+'px';palette.current?.showPopover();}}
 function select(color:string){choose(color);palette.current?.hidePopover();}
 return <div className="editor-color-tool">
  <button type="button" className="editor-color-apply" aria-label={label} aria-pressed={kind==='highlight'?active:undefined} title={label} disabled={disabled} onClick={apply}>{kind==='text'?<Baseline/>:<Highlighter/>}<span style={{background:value==='transparent'?'#fff':value}}/></button>
  <button type="button" className="editor-color-arrow" ref={arrow} aria-label={kind==='text'?'Text colors':'Highlight colors'} aria-haspopup="menu" aria-expanded={open} disabled={disabled} onClick={toggle}><ChevronDown/></button>
  <div ref={palette} popover="auto" className="editor-color-palette" role="menu" aria-label={kind==='text'?'Text color palette':'Highlight color palette'} onToggle={e=>setOpen((e.nativeEvent as ToggleEvent).newState==='open')} onMouseDown={e=>{if((e.target as HTMLElement).closest('button'))e.preventDefault();}}>
   <strong>{kind==='text'?'Text color':'Text highlight color'}</strong>
   {kind==='text'&&<button type="button" role="menuitem" className="palette-option" onClick={()=>select('#202020')}><span style={{background:'#202020'}}/>Automatic</button>}
   <div className="palette-swatches">{swatches.map(([name,color])=><button type="button" key={color} role="menuitemradio" aria-label={name} aria-checked={value.toLowerCase()===color} title={name} style={{background:color}} onClick={()=>select(color)}/>)}</div>
   {kind==='highlight'&&<><button type="button" role="menuitem" className="palette-option" onClick={()=>select('transparent')}><span className="no-color-swatch"/>No color</button><button type="button" role="menuitem" className="palette-option" onClick={()=>{stop?.();palette.current?.hidePopover();}}>Stop highlighting</button></>}
   <label className="palette-custom">More colors<input type="color" aria-label={kind==='text'?'Custom text color':'Custom highlight color'} value={/^#[0-9a-f]{6}$/i.test(value)?value:'#ffff00'} onChange={e=>select(e.target.value)}/></label>
  </div>
 </div>;
}
