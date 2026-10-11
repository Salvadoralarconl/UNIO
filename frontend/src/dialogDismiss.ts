// Use each dialog's existing cancellation handler, including its busy-state guard.
document.addEventListener('click',event=>{
 const target=event.target;
 if(!(target instanceof Element))return;
 if(target instanceof HTMLDialogElement&&target.open){
  const box=target.getBoundingClientRect();
  if(event.clientX<box.left||event.clientX>box.right||event.clientY<box.top||event.clientY>box.bottom){
   if(target.dispatchEvent(new Event('cancel',{cancelable:true})))target.close();
  }
 }else if(target.classList.contains('modal-backdrop')){
  target.querySelector<HTMLButtonElement>('button.close:not(:disabled)')?.click();
 }
});
