import {useEffect,useRef,useState} from 'react';
import {Plus,Users,Mail} from 'lucide-react';
import AssetCategoryInput from './AssetCategoryInput';
import {useCompanySession} from './CompanySession';
import {accountRequest,accountScope,AccountError,activity,type Account,type AccountInput,type Invitation,type Page,type Seats,type ProvisionedAccount,type CredentialLink} from './accountApi';
export function SeatSummary({seats,online,listed=false}:{seats:Seats;online?:number;listed?:boolean}){return <div className="account-summary"><span><strong>{seats.enabled}</strong> enabled</span><span><strong>{seats.pending}</strong> pending invitations</span>{online!==undefined&&<span><strong>{online}</strong> {listed?'online in listed accounts':'online'}</span>}<span>{seats.remaining===null?'Unlimited seats':`${seats.remaining} remaining`}</span>{seats.overLimit&&<p role="status">Over seat limit. Existing accounts are preserved; new seats are blocked.</p>}</div>;}
export const blank:AccountInput={firstName:'',lastName:'',email:'',role:'Viewer',enabled:true};
export function IdentityFields({value,change,disabled,identity=true}:{value:AccountInput;change:(v:AccountInput)=>void;disabled:boolean;identity?:boolean}){const {session}=useCompanySession();const local=session.authenticationMode!=='Oidc';return <>{local?<>{(['firstName','lastName'] as const).map((key,index)=><label key={key}>{index?'Last name':'First name'}<input aria-label={index?'Last name':'First name'} required maxLength={100} value={value[key]??''} disabled={disabled} onChange={e=>change({...value,[key]:e.target.value})}/></label>)}</>:<>{identity&&<><label>OIDC issuer<input aria-label="OIDC issuer" type="url" required value={value.issuer??''} disabled={disabled} onChange={e=>change({...value,issuer:e.target.value})}/></label><label>OIDC subject<input aria-label="OIDC subject" required value={value.subject??''} disabled={disabled} onChange={e=>change({...value,subject:e.target.value})}/></label></>}<label>Display name<input aria-label="Display name" required maxLength={200} value={value.displayName??''} disabled={disabled} onChange={e=>change({...value,displayName:e.target.value})}/></label></>}<label>Email<input aria-label="Email" type="email" value={value.email??''} disabled={disabled} onChange={e=>change({...value,email:e.target.value})}/></label></>;}
export function OnceCredentialLink({link,dismiss}:{link:string;dismiss:()=>void}){const [copied,setCopied]=useState(false),[error,setError]=useState('');return <div className="account-invite-link"><p>Copy this credential link now. It expires after 30 minutes and will not be shown again after leaving this page.</p><input aria-label="Credential link" readOnly value={link} onFocus={e=>e.target.select()}/><button onClick={()=>void navigator.clipboard.writeText(link).then(()=>setCopied(true)).catch(()=>setError('Select and copy the link manually.'))}>{copied?'Copied':'Copy credential link'}</button><button onClick={dismiss}>Dismiss link</button>{error&&<p role="alert">{error}</p>}</div>;}
function DeleteAccountDialog({account,busy,error,cancel,confirm}:{account:Account;busy:boolean;error:string;cancel:()=>void;confirm:()=>void}) {
 const dialog=useRef<HTMLDialogElement>(null);
 useEffect(()=>{const node=dialog.current;node?.showModal();return()=>node?.close();},[]);
 return <dialog ref={dialog} className="account-delete-dialog" aria-labelledby="delete-account-title" onCancel={e=>{e.preventDefault();if(!busy)cancel();}}><h3 id="delete-account-title">Delete account for {account.displayName}?</h3><p>This removes {account.displayName}’s access to this company immediately and frees any enabled seat. Their identity and historical records are preserved.</p><p>{account.email}</p>{error&&<p role="alert">{error}</p>}<footer><button autoFocus disabled={busy} onClick={cancel}>Cancel</button><button className="account-danger" disabled={busy} onClick={confirm}>{busy?'Deleting…':'Delete account'}</button></footer></dialog>;
}
export default function CompanyAccountSettings({company,onChanged}:{company?:string;onChanged?:()=>void}){
 const {session,permissions}=useCompanySession();const operator=!!company&&session.isOperator;
 const allowed=operator||!company&&permissions.accountAdmin;
 const scope=accountScope(company);const local=session.authenticationMode!=='Oidc';
 const [credentialLink,setCredentialLink]=useState('');
 const [deleteTarget,setDeleteTarget]=useState<Account|null>(null);
 const controller=useRef<AbortController|null>(null);
 const mutationController=useRef<AbortController|null>(null);
 const searchRef=useRef('');
 const [accounts,setAccounts]=useState<Account[]>([]),[invitations,setInvitations]=useState<Invitation[]>([]),[seats,setSeats]=useState<Seats|null>(null);
 const [search,setSearch]=useState(''),[cursor,setCursor]=useState<string|null>(null),[inviteCursor,setInviteCursor]=useState<string|null>(null),[loading,setLoading]=useState(true),[mutationBusy,setMutationBusy]=useState(false),[pagingBusy,setPagingBusy]=useState(false),[error,setError]=useState('');
 const [edit,setEdit]=useState<Account|null|undefined>(undefined),[draft,setDraft]=useState<AccountInput>(blank),[invite,setInvite]=useState(false),[email,setEmail]=useState(''),[role,setRole]=useState<'Editor'|'Viewer'>('Viewer'),[link,setLink]=useState(''),[copied,setCopied]=useState(false);
 const busy=mutationBusy||pagingBusy;
 searchRef.current=search;

 async function load(signal:AbortSignal) {
  setLoading(true);
  try {
   const [a,i,s]=await Promise.all([
    accountRequest<Page<Account>>(scope+'/accounts?search='+encodeURIComponent(searchRef.current),'GET',undefined,signal),
    accountRequest<Page<Invitation>>(scope+'/invitations','GET',undefined,signal),
    company?accountRequest<{company:{seats:Seats}}>(scope,'GET',undefined,signal).then(v=>v.company.seats)
     :accountRequest<Seats>(scope+'/seats','GET',undefined,signal)
   ]);
   if(signal.aborted)return;
   setAccounts(a.items);
   setCursor(a.nextCursor);
   setInvitations(i.items);
   setInviteCursor(i.nextCursor);
   setSeats(s);
  } catch(e) {
   if(!signal.aborted)setError((e as Error).message);
  } finally {
   if(!signal.aborted)setLoading(false);
  }
 }
 function refresh() {
  controller.current?.abort();
  const c=new AbortController();
  controller.current=c;
  return load(c.signal);
 }
 // Mutations belong to this company/component, independently of account queries.
 useEffect(()=>{
  if(!allowed)return;
  const c=new AbortController();
  mutationController.current=c;
  setMutationBusy(false);
  setLink('');
  setCredentialLink('');
  setInvite(false);
  setEdit(undefined);
  return()=>{
   c.abort();
   controller.current?.abort();
  };
 },[scope,allowed,session.activeCompany?.id,session.user.id]);
 useEffect(()=>{
  if(!allowed)return;
  setPagingBusy(false);
  setError('');
  void refresh();
  return()=>controller.current?.abort();
 },[scope,search,allowed,session.activeCompany?.id,session.user.id]);

 async function mutate(action:(signal:AbortSignal)=>Promise<unknown>,done?:()=>void,conflicted?:()=>void) {
  const signal=mutationController.current?.signal;
  if(!signal||signal.aborted||busy)return;
  setMutationBusy(true);
  setError('');
  try {
   await action(signal);
   if(signal.aborted)return;
   done?.();
   // Refresh uses the current search and supersedes older list responses.
   await refresh();
   if(!signal.aborted)onChanged?.();
  } catch(e) {
   if(signal.aborted)return;
   setError((e as Error).message);
   if(e instanceof AccountError&&e.status===409) {
    conflicted?.();
    if(conflicted)setError((e as Error).message+' Review the refreshed account and confirm deletion again.');
    if(e.usage)setSeats(e.usage);
    await refresh();
   }
  } finally {
   if(!signal.aborted)setMutationBusy(false);
  }
 }
 async function more(resource:'accounts'|'invitations') {
  const signal=controller.current?.signal;
  if(!signal||signal.aborted||busy)return;
  setPagingBusy(true);
  try {
   if(resource==='accounts') {
    const p=await accountRequest<Page<Account>>(scope+'/accounts?search='+encodeURIComponent(search)+'&cursor='+encodeURIComponent(cursor!),'GET',undefined,signal);
    if(signal.aborted)return;
    setAccounts(a=>[...a,...p.items]);
    setCursor(p.nextCursor);
   } else {
    const p=await accountRequest<Page<Invitation>>(scope+'/invitations?cursor='+encodeURIComponent(inviteCursor!),'GET',undefined,signal);
    if(signal.aborted)return;
    setInvitations(a=>[...a,...p.items]);
    setInviteCursor(p.nextCursor);
   }
  } catch(e) {
   if(!signal.aborted)setError((e as Error).message);
  } finally {
   if(!signal.aborted)setPagingBusy(false);
  }
 }
 async function issueInvitation(signal:AbortSignal) {
  const issued=await accountRequest<Invitation&{link:string}>(scope+'/invitations','POST',{email,role},signal);
  if(signal.aborted)return;
  setLink(new URL(issued.link,window.location.origin).href);
  setCopied(false);
 }
 function invitationDone() {
  setInvite(false);
  setEmail('');
 }
 if(!allowed)return <p>Account administration access is required.</p>;
 return <section className={'account-management'+(!company?' account-panel':'')} aria-label="Company accounts"><header className="account-heading"><div><h2><Users size={20}/>Company accounts</h2><p>Enabled accounts and pending invitations consume seats. Online activity is separate.</p></div><button className="account-primary" disabled={busy} onClick={()=>{setEdit(null);setDraft(local?{...blank}:{issuer:'',subject:'',displayName:'',email:'',role:'Viewer',enabled:true});}}><Plus size={16}/>Add account</button></header>
 {seats&&<SeatSummary seats={seats} online={accounts.filter(a=>a.online).length} listed/>}{error&&<div role="alert">{error} <button disabled={busy} onClick={()=>{if(controller.current)void load(controller.current.signal);}}>Retry accounts</button></div>}
 <label className="account-search">Search accounts<input type="search" aria-label="Search accounts" value={search} onChange={e=>setSearch(e.target.value)}/></label>{loading&&<p role="status">Loading accounts…</p>}
 <div className="account-table-wrap"><table><thead><tr><th>Account</th><th>Role</th><th>Status</th><th>Presence</th><th>Last activity</th><th>Actions</th></tr></thead><tbody>{accounts.map(a=><tr key={a.id}><td><strong>{a.displayName}</strong><small>{a.email??'No email'}</small></td><td>{a.role}</td><td>{a.enabled?(local&&!a.localProfile?.passwordSet?'Enabled · password pending':'Enabled'):'Disabled'}</td><td>{a.online?'Online':'Offline'}</td><td>{activity(a.lastActivity)}</td><td>{a.id!==session.user.id&&(operator||a.role!=='Administrator')&&<button aria-label={'Edit '+a.displayName} disabled={busy} onClick={()=>{setEdit(a);setDraft({...(local?{firstName:a.localProfile?.firstName??'',lastName:a.localProfile?.lastName??''}:{displayName:a.displayName}),email:a.email,role:a.role,enabled:a.enabled,revision:a.revision});}}>Edit</button>}{local&&a.enabled&&a.id!==session.user.id&&(operator||a.role!=='Administrator')&&<button disabled={busy} aria-label={'Issue credential link for '+a.displayName} onClick={()=>void mutate(async signal=>{const result=await accountRequest<CredentialLink>(scope+'/accounts/'+encodeURIComponent(a.id)+'/credential-link','POST',{revision:a.revision},signal);if(!signal.aborted)setCredentialLink(new URL(result.link,window.location.origin).href);})}>{a.localProfile?.passwordSet?'Reset password':'Set up password'}</button>}{a.id!==session.user.id&&['Editor','Viewer'].includes(a.role)&&<button className="account-danger" disabled={busy||loading} aria-label={'Delete account for '+a.displayName} onClick={()=>setDeleteTarget(a)}>Delete account</button>}</td></tr>)}</tbody></table></div>{!loading&&!accounts.length&&<p>No accounts found.</p>}{cursor&&<button disabled={busy} onClick={()=>void more('accounts')}>More accounts</button>}
 {deleteTarget&&<DeleteAccountDialog account={deleteTarget} busy={busy} error={error} cancel={()=>setDeleteTarget(null)} confirm={()=>void mutate(signal=>accountRequest(scope+'/accounts/'+encodeURIComponent(deleteTarget.id),'DELETE',{revision:deleteTarget.revision},signal),()=>{setDeleteTarget(null);setEdit(undefined);setCredentialLink('');},()=>setDeleteTarget(null))}/>}
 {edit!==undefined&&<form className="account-form" aria-label={edit?'Edit account':'New account'} onSubmit={e=>{e.preventDefault();void mutate(async signal=>{const result=await accountRequest<ProvisionedAccount>(scope+'/accounts'+(edit?'/'+encodeURIComponent(edit.id):''),edit?'PUT':'POST',draft,signal);if(!signal.aborted&&!edit&&local)setCredentialLink(result.setup?new URL(result.setup.link,window.location.origin).href:'');},()=>setEdit(undefined));}}><h3>{edit?'Edit account':'New account'}</h3><p>{edit?'Update the account using its current revision.':local?'Enter the recipient’s first and last name. Share the setup link privately.':'Use the verified identity supplied by your configured sign-in provider.'}</p><IdentityFields value={draft} change={setDraft} disabled={busy} identity={!edit}/>{draft.role==='Administrator'?<p>Administrator · assigned by Operator</p>:<AssetCategoryInput label="Account role" required allowCustom={false} value={draft.role} options={['Editor','Viewer']} disabled={busy} onChange={r=>setDraft({...draft,role:r as Account['role']})}/>}<label className="account-check"><input type="checkbox" checked={draft.enabled} disabled={busy} onChange={e=>setDraft({...draft,enabled:e.target.checked})}/>Enabled</label><footer><button type="button" disabled={busy} onClick={()=>setEdit(undefined)}>Cancel</button><button className="account-primary" disabled={busy}>Save account</button></footer></form>}
 {credentialLink&&<OnceCredentialLink key={credentialLink} link={credentialLink} dismiss={()=>setCredentialLink('')}/>}
 <header className="account-heading"><h3><Mail size={18}/>Invitations</h3><button disabled={busy} onClick={()=>{setInvite(true);setLink('');setCopied(false);}}>Invite account</button></header><p>Links expire after seven days. Share each link manually with its intended recipient; no email is sent.</p>
 {invite&&<form className="account-form" aria-label="Invite account" onSubmit={e=>{
  e.preventDefault();
  void mutate(issueInvitation,invitationDone);
 }}>
  <label>Invitation email<input aria-label="Invitation email" type="email" required value={email}
   disabled={busy} onChange={e=>setEmail(e.target.value)}/></label>
  <AssetCategoryInput label="Invitation role" required allowCustom={false} value={role}
   options={['Editor','Viewer']} disabled={busy} onChange={r=>setRole(r as typeof role)}/>
  <footer>
   <button type="button" disabled={busy} onClick={()=>setInvite(false)}>Cancel</button>
   <button className="account-primary" disabled={busy}>Create invitation</button>
  </footer>
 </form>}
 {link&&<div className="account-invite-link"><p>Copy this link now. It will not be shown again after leaving this page.</p><input aria-label="Invitation link" readOnly value={link} onFocus={e=>e.target.select()}/><button onClick={()=>void navigator.clipboard.writeText(link).then(()=>setCopied(true)).catch(()=>setError('Select and copy the invitation link manually.'))}>{copied?'Copied':'Copy invitation link'}</button><button onClick={()=>setLink('')}>Dismiss link</button></div>}
 <div className="account-table-wrap"><table><thead><tr><th>Email</th><th>Role</th><th>Status</th><th>Expires</th><th>Actions</th></tr></thead><tbody>{invitations.map(i=><tr key={i.id}><td>{i.email}</td><td>{i.role}</td><td>{i.status}</td><td>{activity(i.expiresAt)}</td><td>{['Pending','Expired'].includes(i.status)&&<button disabled={busy} aria-label={'Revoke '+i.email} onClick={()=>void mutate(signal=>accountRequest(scope+'/invitations/'+encodeURIComponent(i.id),'DELETE',{revision:i.revision},signal))}>Revoke</button>}</td></tr>)}</tbody></table></div>{!loading&&!invitations.length&&<p>No invitations.</p>}{inviteCursor&&<button disabled={busy} onClick={()=>void more('invitations')}>More invitations</button>}
 </section>;
}
