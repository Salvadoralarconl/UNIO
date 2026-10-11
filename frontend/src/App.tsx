import TicketConfiguration from './tickets/TicketConfiguration';
import InventoryReports from './InventoryReports';
import {accountInitials} from './accountIdentity';
import CompanySession,{useCompanySession} from './CompanySession';
import ChatHistory from './ChatHistory';
import Inventory from './Inventory';
import InventoryOverview from './InventoryOverview';
import InventorySettings from './InventorySettings';
import AssetConfigurationScreen from './AssetConfiguration';
import EquipmentKits from './EquipmentKits';
import EquipmentRequests from './EquipmentRequests';
import Tickets from './tickets/Tickets';
import type {TicketPrefill} from './tickets/ticketApi';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { BookOpenText, MessageCircle, Ticket, Info, Paperclip, FileText, ExternalLink, Upload, X, History, Plus, Box, Pin, LayoutDashboard, ChartNoAxesCombined, Package, ClipboardList, Settings, SlidersHorizontal, LockKeyhole, MonitorUp, UserPlus } from 'lucide-react';
import Documentation from './Documentation';
import { api, getHealth, type Health, type Answer, type Passage, type DocumentInfo, type Citation } from './api';
type ChatTurn = {question: string; answer: Answer; sources: Passage[]};
type ActionRetry = {signature:string;id:string;body:{question:string;conversationId:string|null;taskId:string|null;requestId:string;confirmationToken?:string}};
type Conversation = {retry?:ActionRetry|null;id: string; conversationId: string | null; title: string; turns: ChatTurn[];pinned?:boolean;archived?:boolean;projectId?:string|null;pinnedFolderId?:string|null};

function Star({ small = false }: { small?: boolean }) { return <svg className={`star ${small ? 'small' : ''}`} viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id={small?'star-small':'star-brand'} x1="0" y1="1" x2="1" y2="0"><stop offset="0" stopColor="#70d9ed"/><stop offset=".45" stopColor="#a8b8ff"/><stop offset=".7" stopColor="#d89cff"/><stop offset="1" stopColor="#f8b0e8"/></linearGradient><radialGradient id={small?'glow-small':'glow-brand'}><stop stopColor="white" stopOpacity=".75"/><stop offset="1" stopColor="white" stopOpacity="0"/></radialGradient></defs><path d="M32 3 C39 3 36 19 43 23 C49 27 61 26 61 32 C61 38 49 37 43 41 C36 45 39 61 32 61 C25 61 28 45 21 41 C15 37 3 38 3 32 C3 26 15 27 21 23 C28 19 25 3 32 3Z" fill={`url(#${small?'star-small':'star-brand'})`}/><ellipse cx="32" cy="29" rx="18" ry="22" fill={`url(#${small?'glow-small':'glow-brand'})`}/></svg>; }

export default function App(){return <CompanySession><WorkspaceApp/></CompanySession>;} 
function WorkspaceApp() {
 const {session,permissions,openAccountSettings}=useCompanySession();const initials=accountInitials(session.user.displayName);
 const navigationKey='brie-navigation:'+session.user.id+':'+session.activeCompany!.id;
 const [restored]=useState(()=>{try{return JSON.parse(sessionStorage.getItem(navigationKey)??'{}') as {module?:string;section?:string;document?:string|null;ticketConfiguration?:boolean};}catch{return {};}});
  const [inventorySection,setInventorySection]=useState(()=>['Overview','Inventory','Onboarding','Reports','Kits','Requests','Configuration',...(permissions.settings?['Settings']:[])].includes(restored.section??'')&&(permissions.knowledge||!['Overview','Reports','Onboarding'].includes(restored.section??''))?(restored.section==='Onboarding'?'Requests':restored.section!):permissions.knowledge?'Overview':'Inventory');
  const [onboardingRequest,setOnboardingRequest]=useState<string|null>(null);
  const [ticketConfiguration,setTicketConfiguration]=useState(!!restored.ticketConfiguration&&permissions.write);
  const [activeTicket,setActiveTicket]=useState<string|null>(null),[ticketPrefill,setTicketPrefill]=useState<TicketPrefill|null>(null);
  useEffect(()=>{function open(e:Event){setTicketConfiguration(false);setActiveTicket((e as CustomEvent<string>).detail);setModule('Tickets');}function create(e:Event){setTicketConfiguration(false);setTicketPrefill((e as CustomEvent<TicketPrefill>).detail);setModule('Tickets');}window.addEventListener('open-ticket',open);window.addEventListener('create-ticket',create);return()=>{window.removeEventListener('open-ticket',open);window.removeEventListener('create-ticket',create);};},[]);
  const [inventoryFilters,setInventoryFilters]=useState<Record<string,string>>({});
  const [inventoryDocument,setInventoryDocument]=useState<string|null>(restored.document??null);
  const [health, setHealth] = useState<Health | null>(null);
  const [error, setError] = useState('');
  const [question, setQuestion] = useState('');
  const [linkedEmployee,setLinkedEmployee]=useState<string|null>(null);
  const ticketTaskKey=navigationKey+':brie-ticket-task';
  const [ticketTaskId,setTicketTaskId]=useState<string|null>(()=>sessionStorage.getItem(ticketTaskKey));
  const [brieReady,setBrieReady]=useState(false);
  useEffect(()=>{api<{tickets:boolean}>('/api/brie/capabilities').then(c=>setBrieReady(c.tickets)).catch(()=>{});},[]);
  const retryKey=ticketTaskKey+":retry";const actionRetry=useRef<ActionRetry|null>((()=>{try{return JSON.parse(sessionStorage.getItem(retryKey)??"null");}catch{return null;}})());
  useEffect(()=>{if(ticketTaskId)sessionStorage.setItem(ticketTaskKey,ticketTaskId);else sessionStorage.removeItem(ticketTaskKey);},[ticketTaskId,ticketTaskKey]);
  useEffect(()=>{const target=new URLSearchParams(window.location.search).get('ticket');if(target&&/^[0-9a-f-]{36}$/i.test(target)){setActiveTicket(target);setModule('Tickets');}const params=new URLSearchParams(window.location.search);if(params.has('asset')){setModule('Inventory');setInventorySection('Inventory');}const employee=params.get('employee');if(employee&&/^[0-9a-f-]{36}$/i.test(employee)){setLinkedEmployee(employee);setModule('Inventory');setInventorySection('Configuration');}},[]);
  const [asked, setAsked] = useState('');
  const [conversationId, setConversationId] = useState<string | null>(()=>actionRetry.current?.body?.conversationId??null);
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeChat, setActiveChat] = useState<string | null>(null);
  const end = useRef<HTMLDivElement>(null);
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { end.current?.scrollIntoView({block: 'end'}); }, [turns, busy]);
  const [indexing, setIndexing] = useState(false);
  const [sources, setSources] = useState<Passage[]>([]);
  const [selected, setSelected] = useState<Passage | null>(null);
  const [documents, setDocuments] = useState<DocumentInfo[]>([]);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadTitle, setUploadTitle] = useState('');
  const [uploadVersion, setUploadVersion] = useState('1.0');
  const [preview, setPreview] = useState<{title: string; version: string; text: string} | null>(null);
  const [view, setView] = useState<'assistant' | 'knowledge'>(restored.module==='Documentation'&&permissions.knowledge?'knowledge':'assistant');
  const [module, setModule] = useState<'Clary'|'Documentation'|'Tickets'|'Inventory'|'Vault'|'Remote Support'>(()=>{const allowed=['Inventory','Tickets','Vault','Remote Support',...(permissions.knowledge?['Clary','Documentation']:[])];return allowed.includes(restored.module??'')?restored.module as 'Clary'|'Documentation'|'Tickets'|'Inventory'|'Vault'|'Remote Support':permissions.knowledge?'Clary':'Inventory';});
 useEffect(()=>{try{sessionStorage.setItem(navigationKey,JSON.stringify({module,section:inventorySection,document:inventoryDocument,ticketConfiguration}));}catch{}},[navigationKey,module,inventorySection,inventoryDocument,ticketConfiguration]);
  const [historyMode,setHistoryMode]=useState('recent'),[sharedView,setSharedView]=useState(false);
  const [historyOpen,setHistoryOpen]=useState(false);
  const historyDrawer=useRef<HTMLElement>(null);
  useEffect(()=>{
    if(!historyOpen)return;
    const previous=document.activeElement as HTMLElement|null;
    const drawer=historyDrawer.current;
    drawer?.querySelector<HTMLButtonElement>('button')?.focus();
    function containFocus(event:KeyboardEvent){
      if(event.key==='Escape'){event.preventDefault();setHistoryOpen(false);return;}
      if(event.key!=='Tab'||!drawer)return;
      const controls=[...drawer.querySelectorAll<HTMLElement>('button:not(:disabled),[tabindex="0"]')];
      const first=controls[0],last=controls.at(-1);
      if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus();}
      else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus();}
    }
    document.addEventListener('keydown',containFocus);
    return ()=>{document.removeEventListener('keydown',containFocus);previous?.focus();};
  },[historyOpen]);
  const [sourcesOpen,setSourcesOpen]=useState(true);
  function changeModule(next: typeof module) {if(next==='Inventory'&&module!=='Inventory'){setInventorySection(permissions.knowledge?'Overview':'Inventory');setInventoryFilters({});}setModule(next);setHistoryOpen(false);setView(next==='Documentation'?'knowledge':'assistant');if(next==='Documentation')void refreshDocuments();}
  function newConversation() {
    if (busy) return;
    setConversations(previous=>previous.map(c=>c.id===activeChat?{...c,retry:actionRetry.current}:c));setTicketTaskId(null);actionRetry.current=null;sessionStorage.removeItem(retryKey);setSharedView(false);if(new URLSearchParams(window.location.search).has('share'))window.history.replaceState(null,'',window.location.pathname);setModule('Clary');setHistoryOpen(false);setView('assistant'); setActiveChat(null); setTurns([]); setConversationId(null); setAsked(''); setAnswer(null); setSources([]); setSelected(null); setError(''); setQuestion('');
  }
  async function resumeConversation(chat: Conversation) {
    if (busy) return;
    if(!chat.turns.length && chat.conversationId) {
      setBusy(true);setError('');
      try {
        const saved=await api<{question:string; answer:Answer}[]>(`/api/conversations/${chat.conversationId}`);
        const ids=[...new Set(saved.flatMap(t=>[...t.answer.citations,...t.answer.steps.flatMap(s=>s.citations)].map(c=>c.passageId)))];
        const passages=await Promise.all(ids.map(id=>api<Passage>(`/api/passages/${id}`).catch(()=>null)));
        const restored=saved.map(t=>({...t,sources:passages.filter((p):p is Passage=>p!==null).filter(p=>[...t.answer.citations,...t.answer.steps.flatMap(s=>s.citations)].some(c=>c.passageId===p.id))}));
        chat={...chat,turns:restored};setConversations(previous=>previous.map(c=>c.id===chat.id?chat:c));
      } catch(e){setError((e as Error).message);return;} finally{setBusy(false);}
    }
    const pending=chat.turns.map(t=>t.answer.ticketTask).filter(t=>!!t).at(-1);setTicketTaskId(pending&&!['completed','paused'].includes(pending.status)?pending.taskId:null);actionRetry.current=chat.retry??null;if(actionRetry.current)sessionStorage.setItem(retryKey,JSON.stringify(actionRetry.current));else sessionStorage.removeItem(retryKey);
    setSharedView(false);setModule('Clary');setHistoryOpen(false);setView('assistant'); setActiveChat(chat.id); setConversationId(chat.conversationId); setTurns(chat.turns); setAsked(''); setQuestion(''); setSelected(null); setError('');
    const last = chat.turns.at(-1); setAnswer(last?.answer ?? null); setSources(last?.sources ?? []);
  }
  async function refreshChats(){const saved=await api<Conversation[]>('/api/conversations');setConversations(previous=>saved.map(c=>({...c,id:previous.find(p=>p.conversationId===c.id)?.id??c.id,conversationId:c.id,retry:previous.find(p=>p.conversationId===c.id)?.retry??null,turns:previous.find(p=>p.conversationId===c.id)?.turns??[]})));}
  useEffect(()=>{const token=new URLSearchParams(window.location.search).get('share');if(!token)return;setSharedView(true);api<{title:string;turns:{question:string;answer:Answer}[]}>(`/api/shared/conversations/${encodeURIComponent(token)}`).then(shared=>{setTurns(shared.turns.map(t=>({...t,sources:[]})));setAnswer(shared.turns.at(-1)?.answer??null);}).catch(e=>setError((e as Error).message));},[]);
  useEffect(()=>{if(!brieReady||!ticketTaskId)return;api<NonNullable<Answer['ticketTask']>>(`/api/brie/tickets/${ticketTaskId}`).then(task=>{if(task.status==='completed'){setTicketTaskId(null);return;}setTurns(previous=>previous.length?previous:[{question:'Resumed Brie task',answer:{status:'action',questions:[],steps:[],citations:[],origin:'Brie',error:null,evidenceNote:task.message,ticketTask:task},sources:[]}]);}).catch(e=>setError((e as Error).message));},[brieReady]);
  async function refreshDocuments() { try { setDocuments(await api<DocumentInfo[]>('/api/documents')); } catch (e) { setError((e as Error).message); } }
  async function indexSamples() { if(!permissions.index)return; setIndexing(true); setError(''); try { await api('/api/samples/index', {}); await refreshDocuments(); } catch (e) { setError((e as Error).message); } finally { setIndexing(false); } }
  async function upload(e: FormEvent) {
    e.preventDefault(); if (!uploadFile || indexing) return;
    setIndexing(true); setError('');
    try {
      const form = new FormData(); form.append('file', uploadFile); form.append('title', uploadTitle); form.append('version', uploadVersion);
      await api<DocumentInfo>('/api/documents/upload', form); await refreshDocuments(); setUploadOpen(false); setUploadFile(null); setUploadTitle('');
    } catch (e) { setError((e as Error).message); } finally { setIndexing(false); }
  }
  async function approveDocument(id: string) {
    if(!permissions.approve)return;
    setIndexing(true); setError('');
    try { await api(`/api/documents/${id}/approve`, {}); } catch (e) { setError((e as Error).message); }
    finally { await refreshDocuments(); setIndexing(false); }
  }
  async function reviewDocument(document: DocumentInfo) {
    try { const result = await api<{text: string}>(`/api/documents/${document.id}/text`); setPreview({...document, text: result.text}); } catch (e) { setError((e as Error).message); }
  }
  async function submit(e: FormEvent, explicitMessage?:string, confirmationToken?:string) {
    e.preventDefault(); const message=explicitMessage??question.trim(); if (!permissions.write || !message || busy) return;
    const chatId = activeChat ?? crypto.randomUUID();
    if (!activeChat) { setActiveChat(chatId); setConversations(previous => [{id: chatId, conversationId: null, title: question.trim(), turns: []}, ...previous]); }
    setBusy(true); setError(''); setAsked(question.trim()); setAnswer(null); setSources([]); setSelected(null);
    try {
      const signature=Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256",new TextEncoder().encode(JSON.stringify({message,conversationId,ticketTaskId,confirmationToken}))))).map(b=>b.toString(16).padStart(2,"0")).join("");
      if(!actionRetry.current?.body||actionRetry.current.body.question!==message||actionRetry.current.body.confirmationToken!==confirmationToken){const id=crypto.randomUUID();actionRetry.current={signature,id,body:{question:message,conversationId,taskId:ticketTaskId,requestId:id,confirmationToken}};}sessionStorage.setItem(retryKey,JSON.stringify(actionRetry.current));setConversations(previous=>previous.map(c=>c.id===chatId?{...c,retry:actionRetry.current}:c));
      const result = await api<Answer>('/api/brie/messages', actionRetry.current.body);
      if(result.ticketTask)setTicketTaskId(['completed','paused'].includes(result.ticketTask.status)?null:result.ticketTask.taskId);
      const citations = [...result.citations, ...result.steps.flatMap(s => s.citations)];
      const ids = [...new Set(citations.map(c => c.passageId))];
      const passages = await Promise.all(ids.map(id => api<Passage>(`/api/passages/${encodeURIComponent(id)}`)));
      setSources(passages); setAnswer(result); setConversationId(result.conversationId ?? null);
      const updated = [...turns, {question: message, answer: result, sources: passages}];
      actionRetry.current=null;sessionStorage.removeItem(retryKey);setTurns(updated); setConversations(previous => previous.map(chat => chat.id === chatId ? {...chat, retry:null, conversationId: result.conversationId ?? null, turns: updated} : chat)); setAsked(''); setQuestion('');
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function openSource(id: string) { try { setSelected(await api<Passage>(`/api/passages/${encodeURIComponent(id)}`)); } catch (e) { setSelected(null); setError(`This source could not be opened. It may have been reindexed or become unavailable. Your conversation is preserved. ${(e as Error).message}`); } }
  const citations: Citation[] = answer ? [...answer.citations, ...answer.steps.flatMap(s => s.citations)] : [];
  useEffect(() => {
    const controller = new AbortController();
    getHealth(controller.signal).then(setHealth).catch(e => { if (!controller.signal.aborted) setError(String(e.message)); });
    if(permissions.knowledge){void refreshDocuments();
    api<Conversation[]>('/api/conversations').then(saved=>setConversations(previous=>[
      ...previous,...saved.filter(c=>!previous.some(p=>p.conversationId===c.id)).map(c=>({...c,conversationId:c.id,turns:[]}))
    ])).catch(()=>setError('Saved conversations could not be loaded. Check the local backend and retry.'));}
    return () => controller.abort();
  }, []);
  return <div className={`suite-shell module-${module.toLowerCase().replaceAll(' ','-')} ${sourcesOpen?'':'sources-hidden'}`}>
    <aside className="icon-rail" aria-label={`${module==='Clary'?'Brie':module} tools`}>
      <div className="rail-brand" title="Brie by UNIO"><img className="rail-logo" src="/clary-logo.png" alt="Brie"/></div>
      <nav aria-label={`${module==='Clary'?'Brie':module} navigation`}>
        {module==='Clary'?<>
          <span className="rail-tip"><button aria-label="New chat" disabled={busy} onClick={newConversation}><MessageCircle/></button><span role="tooltip">New chat</span></span>
          <span className="rail-tip"><button aria-label="Previous chats" aria-pressed={historyOpen&&historyMode!=='pinned'} onClick={()=>{setHistoryMode('recent');setHistoryOpen(!(historyOpen&&historyMode!=='pinned'));}}><History/></button><span role="tooltip">Previous chats</span></span><span className="rail-tip"><button aria-label="Pinned chats" aria-pressed={historyOpen&&historyMode==='pinned'} onClick={()=>{setHistoryMode('pinned');setHistoryOpen(!(historyOpen&&historyMode==='pinned'));}}><Pin/></button><span role="tooltip">Pinned chats</span></span>
          <span className="rail-tip"><button aria-label="Toggle sources" aria-pressed={sourcesOpen} onClick={()=>setSourcesOpen(!sourcesOpen)}><FileText/></button><span role="tooltip">Sources</span></span>
        </>:module==='Documentation'?<>
          <span className="rail-tip"><button aria-label="All documents" onClick={()=>void refreshDocuments()}><BookOpenText/></button><span role="tooltip">All documents</span></span>
          {permissions.write&&<span className="rail-tip"><button aria-label="Upload files" disabled={indexing||busy||!permissions.write} onClick={()=>setUploadOpen(true)}><Upload/></button><span role="tooltip">Upload files</span></span>}
        </>:['Tickets','Vault','Remote Support'].includes(module)?<span className="rail-tip"><button aria-label={module==='Tickets'?'Ticket list':module} disabled={module!=='Tickets'} onClick={()=>setTicketConfiguration(false)}>{module==='Tickets'?<Ticket/>:module==='Vault'?<LockKeyhole/>:<MonitorUp/>}</button><span role="tooltip">{module==='Tickets'?'Ticket list':module}</span></span>:<>{([{label:'Overview',Icon:LayoutDashboard},{label:'Inventory',Icon:Box},{label:'Requests',Icon:ClipboardList},{label:'Kits',Icon:Package},{label:'Reports',Icon:ChartNoAxesCombined}]).map(({label,Icon})=><span key={label} className="rail-tip"><button aria-label={label} aria-pressed={label===inventorySection} disabled={!['Overview','Inventory','Onboarding','Reports','Kits','Requests'].includes(label)||(!permissions.knowledge&&['Overview','Reports','Onboarding'].includes(label))} onClick={()=>{setInventorySection(label);setInventoryFilters({});}}><Icon/></button><span role="tooltip">{label}</span></span>)}</>}
      </nav>
      <div className="rail-bottom"><span className="rail-tip"><button aria-label="Configuration" disabled={!['Inventory','Tickets'].includes(module)||module==='Tickets'&&!permissions.write} aria-pressed={module==='Tickets'?ticketConfiguration:module==='Inventory'&&inventorySection==='Configuration'} onClick={()=>{if(module==='Tickets'){setTicketConfiguration(true);void refreshDocuments();}else setInventorySection('Configuration');}}><SlidersHorizontal/></button><span role="tooltip">Configuration</span></span>{permissions.settings&&<span className="rail-tip"><button aria-label="Settings" disabled={module!=='Inventory'} aria-pressed={module==='Inventory'&&inventorySection==='Settings'} onClick={()=>setInventorySection('Settings')}><Settings/></button><span role="tooltip">Settings</span></span>}<button className="avatar" aria-label="Account settings" title="Account settings" onClick={openAccountSettings}>{initials}</button></div>
    </aside>
    <nav className="module-tabs" role="tablist" aria-label="App sections">{(['Clary','Documentation','Inventory','Tickets','Vault','Remote Support'] as const).filter(name=>permissions.knowledge||!['Clary','Documentation'].includes(name)).map(name=><button key={name} role="tab" id={`tab-${name.replaceAll(' ','-')}`} aria-selected={module===name} aria-controls="module-content" onClick={()=>changeModule(name)}>{name==='Clary'?'Brie':name}</button>)}</nav>
    <main id="module-content" role="tabpanel" aria-labelledby={`tab-${module.replaceAll(' ','-')}`} className="workspace">
      {module!=='Documentation'&&<header className="workspace-toolbar"><span className="toolbar-title">{module==='Clary'?<MessageCircle/>:module==='Tickets'?(ticketConfiguration?<SlidersHorizontal/>:<Ticket/>):module==='Vault'?<LockKeyhole/>:module==='Remote Support'?<MonitorUp/>:inventorySection==='Onboarding'?<UserPlus/>:inventorySection==='Overview'?<LayoutDashboard/>:inventorySection==='Configuration'?<SlidersHorizontal/>:inventorySection==='Settings'?<Settings/>:<Box/>}<span>{module==='Clary'?'Brie':module==='Inventory'?inventorySection:module==='Tickets'&&ticketConfiguration?'Configuration':module}</span></span>{module==='Inventory'&&<><div id="inventory-toolbar-search"/><div id="inventory-toolbar-actions"/></>}{module==='Tickets'&&!ticketConfiguration&&<div id="ticket-toolbar-controls"/>}{module==='Clary'&&<div className="toolbar-status"><span className="live-status"><i/>{health?.origin??'Connecting'}</span><span className="approved-status"><FileText/>Approved sources only<Info/></span></div>}</header>}
      {module==='Inventory'?(inventorySection==='Reports'?<InventoryReports openOnboarding={id=>{setOnboardingRequest(id);setInventorySection('Requests');}}/>:inventorySection==='Requests'?<EquipmentRequests initialRequestId={onboardingRequest} openTicket={id=>{setActiveTicket(id);changeModule('Tickets');}}/>:inventorySection==='Kits'?<EquipmentKits/>:inventorySection==='Configuration'?<AssetConfigurationScreen initialEmployeeId={linkedEmployee??undefined}/>:inventorySection==='Settings'?<InventorySettings/>:inventorySection==='Overview'?<InventoryOverview openAssets={filters=>{setInventoryFilters(filters);setInventorySection('Inventory');}}/>:<Inventory key={inventorySection+JSON.stringify(inventoryFilters)} title={inventorySection} initialFilters={inventoryFilters} onOpenDocumentation={id=>{setInventoryDocument(id);void refreshDocuments();changeModule('Documentation');}}/>):module==='Tickets'?ticketConfiguration?<TicketConfiguration documents={documents} back={()=>setTicketConfiguration(false)}/>:<Tickets initialTicketId={activeTicket} prefill={ticketPrefill} documents={documents} clearPrefill={()=>setTicketPrefill(null)} openDocument={id=>{setInventoryDocument(id);changeModule('Documentation');}} openRequest={id=>{setOnboardingRequest(id);changeModule('Inventory');setInventorySection('Requests');}}/>:['Vault','Remote Support'].includes(module)?<section className="module-placeholder"><h1>{module}</h1><p>{module} is not available yet.</p><p>This section will be built in the next phase.</p></section>:<>
      {view === 'knowledge' ? <Documentation openDocumentId={inventoryDocument} documents={documents} indexing={indexing} busy={busy} onUpload={()=>setUploadOpen(true)} onApprove={id=>void approveDocument(id)} onDocumentsChanged={refreshDocuments}/> : <div className={`conversation ${asked?'has-question':''}`}>
        
        {turns.map((turn, i) => <div className="conversation-turn" key={i}><div className="user-message"><span className="user-bubble">{turn.question}</span><span className="avatar">{initials}</span></div><div className="assistant-label"><Star small/><strong>Brie</strong><span className="label">Assistant</span></div><AnswerCard answer={turn.answer} sources={turn.sources} onOpen={id => void openSource(id)} busy={busy} onTicketReply={i===turns.length-1?(text,token)=>void submit({preventDefault(){}} as FormEvent,text,token):undefined}/></div>)}
        {asked && <div className="user-message"><span className="user-bubble">{asked}</span><span className="avatar">{initials}</span></div>}
        {busy ? <article className="answer"><p role="status">Checking approved documentation…</p></article> : !turns.length ? <article className="welcome"><Star/><h2>Let’s get started</h2><p>Describe your issue. Brie searches your approved documentation for supporting guidance.</p><p className="welcome-status" role="status">{health?.configurationError || (health ? 'Backend connected' : 'Connecting to the local backend…')}</p><button className="secondary" onClick={()=>changeModule('Documentation')}>Manage documentation</button><p className="notice">{health?.origin==='Demo workspace'?'Demo mode uses deterministic fixtures.':'Responses use your configured AI provider.'} Grounding checks reduce errors but cannot guarantee accuracy.</p></article> : null}<div ref={end}/>
      </div>}
      {error && <p className="error" role="alert">{error}</p>}
      {module==='Clary'&&<form className="composer" onSubmit={submit}>{permissions.write&&<button type="button" disabled={busy || indexing} onClick={() => {changeModule('Documentation'); setUploadOpen(true);}} aria-label="Upload documentation"><Paperclip/></button>}<input aria-label="Issue description" placeholder="Describe an issue or ask a question…" maxLength={4000} value={question} onChange={e=>setQuestion(e.target.value)} disabled={!permissions.write || sharedView || busy || indexing || view !== 'assistant'}/><button className="send" disabled={!permissions.write || sharedView || busy || indexing || !question.trim() || view !== 'assistant'} aria-label="Send question">Send</button></form>}
      </>}
    {historyOpen&&module==='Clary'&&<div className="history-backdrop" onClick={()=>setHistoryOpen(false)}><aside ref={historyDrawer} className="history-drawer" role="dialog" aria-modal="true" aria-label="Previous chats" onClick={e=>e.stopPropagation()}><header><h2>{historyMode==='pinned'?'Pinned chats':'Previous chats'}</h2><button onClick={()=>setHistoryOpen(false)} aria-label="Close history"><X/></button></header>{permissions.write&&historyMode!=='pinned'&&<button className="primary" disabled={busy} onClick={newConversation}><Plus size={16}/>New conversation</button>}<ChatHistory chats={conversations} mode={historyMode} setMode={setHistoryMode} active={activeChat} busy={busy} resume={chat=>void resumeConversation(chat)} changed={refreshChats} deleted={id=>{if(activeChat===id)newConversation();}}/></aside></div>}
    </main>
    {module==='Clary'&&sourcesOpen&&<aside className="right-panel" aria-label="Sources panel">
      <header className="workspace-toolbar sources-toolbar"><span>Sources</span><span className="source-count">{sources.length} supporting passage{sources.length===1?'': 's'}</span></header><section className="sources">{sources.length ? sources.map(source=><article className="source-card" key={source.id}><div className="source-title"><FileText size={24}/><strong>{source.title}</strong><span className="approved">Approved</span></div><p>v{source.version} · {documents.find(d=>d.id===source.revisionId)?.isUploaded ? 'Uploaded document' : source.title.includes('User-provided') ? 'User-provided guide' : source.title.startsWith('Epson ') ? 'Epson documentation notes' : 'Fictional sample'}</p>{citations.filter(c=>c.passageId===source.id).filter((c,i,a)=>a.findIndex(other=>other.quote===c.quote)===i).map((c,i)=><blockquote key={i}>“{c.quote}”</blockquote>)}<p>{source.location}</p><button className="open-document" onClick={()=>void openSource(source.id)}><ExternalLink size={17}/>Open document</button></article>) : <div className="empty-sources"><FileText size={28}/><h3>Your evidence, in view</h3><p>Approved source passages will appear here after retrieval. No sample response is being presented as live advice.</p></div>}</section>
    </aside>}

    {uploadOpen && <div className="modal-backdrop"><section className="passage-modal" role="dialog" aria-modal="true" aria-label="Upload files"><button className="close" disabled={indexing} onClick={() => setUploadOpen(false)} aria-label="Close upload"><X/></button><h2>Upload files</h2><p>Files remain drafts until you approve and index them.</p><form className="upload-form" onSubmit={upload}><label>Documentation file<input type="file" accept=".pdf,.docx,.txt,.md" required disabled={indexing} onChange={e => {const file=e.target.files?.[0] ?? null; setUploadFile(file); setUploadTitle(file?.name.replace(/\.[^.]+$/, '') ?? '');}}/></label><label>Document title<input value={uploadTitle} maxLength={200} required disabled={indexing} onChange={e => setUploadTitle(e.target.value)}/></label><label>Version<input value={uploadVersion} maxLength={40} required disabled={indexing} onChange={e => setUploadVersion(e.target.value)}/></label>{error && <p role="alert">{error}</p>}<button className="primary" disabled={indexing || !uploadFile}>{indexing ? 'Uploading…' : 'Upload as draft'}</button></form></section></div>}
    {preview && <div className="modal-backdrop" onClick={() => setPreview(null)}><section className="passage-modal" role="dialog" aria-modal="true" aria-label="Document preview" onClick={e => e.stopPropagation()}><button className="close" onClick={() => setPreview(null)} aria-label="Close preview"><X/></button><h2>{preview.title}</h2><p>Version {preview.version} · Full extracted document</p><pre>{preview.text}</pre></section></div>}
    {selected && <div className="modal-backdrop" onClick={()=>setSelected(null)}><section className="passage-modal" role="dialog" aria-modal="true" aria-label="Supporting passage" onClick={e=>e.stopPropagation()}><button className="close" onClick={()=>setSelected(null)} aria-label="Close passage"><X/></button><h2>{selected.title}</h2><p>Version {selected.version} · {selected.location}</p><pre>{selected.text}</pre><p className="notice">Exact extracted passage. Review the full extracted document in Knowledge Base.</p></section></div>}
  </div>;
}


function AnswerCard({answer, sources, onOpen, onTicketReply, busy}: {answer: Answer; sources: Passage[]; onOpen: (id: string) => void; onTicketReply?:(text:string,token?:string)=>void; busy?:boolean}) { return <article className="answer chat-reply">{answer.ticketTask?.recordId && (answer.ticketTask.recordKind==="asset"||answer.ticketTask.recordKind==="employee") && <a href={`/?${answer.ticketTask.recordKind}=${encodeURIComponent(answer.ticketTask.recordId)}`}>Open {answer.ticketTask.recordKind}</a>}{answer.ticketTask?.ticketId && <a href={`/?ticket=${encodeURIComponent(answer.ticketTask.ticketId)}`}>Open ticket</a>}{answer.ticketTask?.choices?.map(choice=><button key={choice.id} disabled={busy||!onTicketReply} onClick={()=>onTicketReply?.(choice.id)}>{choice.label} · {choice.id}</button>)}{answer.ticketTask?.confirmationToken && <button disabled={busy||!onTicketReply} onClick={()=>onTicketReply?.('Confirm',answer.ticketTask!.confirmationToken!)}>Confirm this action</button>}{answer.evidenceNote && <p>{answer.evidenceNote}</p>}{answer.status === 'insufficient' && !answer.evidenceNote && <p>I couldn’t find enough support in the approved documentation to recommend a fix.</p>}{answer.status === 'insufficient' && <p>To create a support ticket, say “Create a support ticket”. Include the issue and attempts you want recorded.</p>}{answer.status === 'conflicting' && <p>These approved sources disagree. Ask IT to confirm the applicable policy before acting.</p>}{answer.status === 'supported' && answer.steps.length > 0 && <p>Try this documented step, then tell me what happened.</p>}{answer.error && <p role="alert">{answer.error}</p>}{answer.questions.map((q,i)=><p key={i}>{q}</p>)}{answer.steps.map((step,i)=><div className="advice-step" key={i}><span className="step-number">{i+1}</span><div><p style={{whiteSpace:"pre-line"}}>{step.text}</p></div><div>{step.citations.map((c,j)=><button className="citation" key={j} onClick={()=>onOpen(c.passageId)} aria-label={`Open citation ${sources.findIndex(s=>s.id===c.passageId)+1}`}>[{sources.findIndex(s=>s.id===c.passageId)+1}]</button>)}</div></div>)}</article>; }
















