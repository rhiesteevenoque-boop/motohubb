(()=>{'use strict';
const M=window.MH;if(!M)return;const db=M.get();
document.addEventListener('DOMContentLoaded',()=>{
 const login=M.$('#login-form');
 if(login) login.addEventListener('submit',e=>{
  e.preventDefault();const f=new FormData(login),email=String(f.get('email')).trim().toLowerCase(),pass=String(f.get('password'));
  const account=db.users.find(x=>x.email.toLowerCase()===email&&x.password===pass);
  if(!account){M.toast('Email or password is incorrect.');return;}
  db.currentUser=account.id;M.save();const next=new URLSearchParams(location.search).get('next');
  location.href=account.role==='admin'?'admin.html':(next&&!['login.html','signup.html','forgot-password.html'].includes(next)?next:'home.html');
 });
 const signup=M.$('#signup-form');
 if(signup) signup.addEventListener('submit',e=>{
  e.preventDefault();const f=new FormData(signup),name=String(f.get('name')).trim(),email=String(f.get('email')).trim().toLowerCase(),phone=String(f.get('phone')).trim(),password=String(f.get('password')),confirm=String(f.get('confirm'));
  if(password.length<8){M.toast('Use a password with at least 8 characters.');return;}
  if(password!==confirm){M.toast('Passwords do not match.');return;}
  if(db.users.some(x=>x.email.toLowerCase()===email)){M.toast('An account with this email already exists.');return;}
  const account={id:M.id(),name,email,phone,password,role:'user'};db.users.push(account);db.currentUser=account.id;
  db.notifications.unshift({id:M.id(),userId:account.id,text:'Welcome to MotoHub. Your account is ready.',date:new Date().toISOString()});M.save();location.href='home.html';
 });
 const forgot=M.$('#forgot-form');
 if(forgot)forgot.addEventListener('submit',e=>{e.preventDefault();M.toast('If this email is registered, password reset instructions would be sent. Email delivery requires a backend.');forgot.reset();});
});})();
