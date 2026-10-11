import AssetCategoryInput from '../AssetCategoryInput';
import TicketAttachments from './TicketAttachments';
import {Pencil,ArrowRightLeft,Trash2} from 'lucide-react';

import {useEffect,useRef,useState} from 'react';

import TicketConnections from './TicketConnections';

import TicketChecklist from './TicketChecklist';
import TicketComments from './TicketComments';

import {localDate,today,type Ticket} from './ticketApi';

export default function TicketDetails({ticket,busy,action,edit,openAsset,openDocument,openRequest,changed}:{ticket:Ticket|null;busy:boolean;action:(action:string)=>void;edit:()=>void;openAsset:(id:string)=>void;openDocument:(id:string)=>void;openRequest:(id:string)=>void;changed:(ticket:Ticket)=>void}){

 const [statusMenu,setStatusMenu]=useState(false);const menuRef=useRef<HTMLDivElement>(null);

 useEffect(()=>{setStatusMenu(false);},[ticket?.id,ticket?.revision]);

 useEffect(()=>{if(!statusMenu)return;const close=(e:MouseEvent)=>{if(!menuRef.current?.contains(e.target as Node))setStatusMenu(false);};const key=(e:KeyboardEvent)=>{if(e.key==='Escape')setStatusMenu(false);};document.addEventListener('click',close);document.addEventListener('keydown',key);return()=>{document.removeEventListener('click',close);document.removeEventListener('keydown',key);};},[statusMenu]);

 if(!ticket)return <section className="ticket-panel ticket-detail" aria-label="Ticket details"><div className="ticket-empty"><p>Select a ticket to view its information.</p></div></section>;

 const active=ticket.allowedActions;const changes=[['approve','Approved'],['start','In Progress'],['send-for-repair','In Progress · Send for repair'],['complete','Complete'],['cancel','Canceled']].filter(([value])=>active.includes(value));

 return <section className="ticket-panel ticket-detail" aria-label="Ticket details"><header><div><span className="ticket-muted">{ticket.type} · {ticket.id.slice(0,8).toUpperCase()}</span><h2>{ticket.title}</h2></div><div className="ticket-detail-title-actions"><span className="ticket-state" data-status={ticket.status}>{ticket.status}</span>{changes.length>0&&<div className="ticket-status-control" ref={menuRef}><button aria-label="Change ticket status" aria-expanded={statusMenu} disabled={busy} onClick={()=>setStatusMenu(!statusMenu)}><ArrowRightLeft size={16}/></button>{statusMenu&&<div className="ticket-status-menu" role="menu">{changes.map(([value,label])=><button role="menuitem" key={value} disabled={busy} onClick={()=>{setStatusMenu(false);action(value);}}><span className="ticket-state" data-status={label.split(' · ')[0]}>{label}</span></button>)}</div>}</div>}{active.includes('delete')&&<button aria-label="Delete setup ticket" title="Delete setup ticket" disabled={busy} onClick={()=>action('delete')}><Trash2 size={15}/></button>}{active.includes('edit')&&<button aria-label="Edit ticket" onClick={edit} disabled={busy}><Pencil size={15}/></button>}</div></header><div className="ticket-detail-scroll"><dl className="ticket-facts"><div><dt>Assigned technicians</dt><dd>{ticket.assignmentMode==='All'?'Everyone':ticket.assignees.map(u=>u.displayName).join(', ')||'—'}</dd></div><div><dt>Equipment recipient</dt><dd>{ticket.recipientName??'—'}</dd></div><div data-priority={ticket.priority??'Normal'}><dt>Priority</dt><dd>{active.includes('priority')?<AssetCategoryInput label="Ticket priority" allowCustom={false} required disabled={busy} value={ticket.priority??'Normal'} options={['Low','Normal','High','Urgent']} onChange={priority=>action('priority:'+priority)}/>:<span className="ticket-priority" data-priority={ticket.priority??'Normal'}>{ticket.priority??'Normal'}</span>}</dd></div><div><dt>Due date</dt><dd>{localDate(ticket.dueDate)} {ticket.dueDate&&ticket.dueDate<today()&&!['Complete','Canceled'].includes(ticket.status)&&<span className="ticket-overdue">Overdue</span>}</dd></div><div><dt>Created by</dt><dd>{ticket.creatorName??'—'}</dd></div><div><dt>Approved by</dt><dd>{ticket.approverName??'—'}</dd></div><div><dt>Approved on</dt><dd>{localDate(ticket.approvedAt)}</dd></div>{ticket.locationName&&<div><dt>Location</dt><dd>{ticket.locationName}</dd></div>}{ticket.departmentName&&<div><dt>Department</dt><dd>{ticket.departmentName}</dd></div>}</dl><section className="ticket-information"><h3>Information</h3><p>{ticket.description||'No additional information.'}</p></section><TicketChecklist ticket={ticket} changed={changed}/><TicketConnections changed={changed} ticket={ticket} openAsset={openAsset} openDocument={openDocument} openRequest={openRequest}/><TicketAttachments key={ticket.id} ticket={ticket}/><TicketComments key={ticket.id+':'+ticket.revision} ticketId={ticket.id} canComment={active.includes('comment')}/></div></section>;

}

