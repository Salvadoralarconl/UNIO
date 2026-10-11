import {useState} from 'react';
import {ticketRequest,localTime,type Ticket} from './ticketApi';
export default function TicketChecklist({ticket,changed}:{ticket:Ticket;changed:(ticket:Ticket)=>void}){
 const [busy,setBusy]=useState(false),[error,setError]=useState('');const items=ticket.checklist??[];if(!items.length)return null;
 async function check(id:string,completed:boolean){if(busy)return;setBusy(true);setError('');try{changed(await ticketRequest<Ticket>('/'+ticket.id+'/checklist/'+id,'PUT',{completed,revision:ticket.revision}));}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 const canCheck=ticket.allowedActions.includes('comment')&&!['Complete','Canceled'].includes(ticket.status);
 return <section className="ticket-checklist"><h3>Checklist <span className="ticket-muted">{items.filter(x=>x.completed).length}/{items.length}</span></h3>{items.map(item=><div key={item.id}><label className="ticket-check"><input type="checkbox" checked={item.completed} disabled={busy||!canCheck} onChange={e=>void check(item.id,e.target.checked)}/><span>{item.text}</span></label>{item.completedBy&&<p className="ticket-muted">{item.completedBy} · {item.completedAt?localTime(item.completedAt):''}</p>}</div>)}{error&&<p role="alert">{error}</p>}</section>;
}
