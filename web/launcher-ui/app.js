(() => {
"use strict";
const state={servers:[],favorites:new Set(),history:[],last:null};
const $=id=>document.getElementById(id), storageKey="serpf.community-dashboard.v2";
try{const saved=JSON.parse(localStorage.getItem(storageKey)||"{}");if(Array.isArray(saved.favorites))saved.favorites.filter(x=>typeof x==="string").forEach(x=>state.favorites.add(x));if(Array.isArray(saved.history))state.history=saved.history.filter(x=>typeof x==="string").slice(0,8);if(typeof saved.last==="string")state.last=saved.last;}catch{}
function save(){try{localStorage.setItem(storageKey,JSON.stringify({favorites:[...state.favorites],history:state.history,last:state.last}));}catch{}}
function online(s){return String(s.status||"").toLowerCase()==="online";}
function send(message){const bridge=window.chrome&&window.chrome.webview;
if(bridge)bridge.addEventListener("message",event=>{
 const m=event.data;if(!m||m.version!==1)return;
 if(m.type==="servers.updated"&&Array.isArray(m.servers)){
  state.servers=m.servers.filter(s=>s&&typeof s.id==="string"&&typeof s.name==="string");
  $("status").textContent=state.servers.length+" communities received from the launcher directory.";
  $("connection-state").textContent="DIRECTORY CONNECTED";
  $("directory-status").textContent=state.servers.length+" communities currently published.";render();
 }else if(m.type==="settings.updated"){
  $("directory-url").value=typeof m.directoryUrl==="string"?m.directoryUrl:"";
  $("game-path").value=typeof m.gamePath==="string"?m.gamePath:"";
  $("steam-account").textContent=m.steamId?"STEAM "+m.steamId:"";
  $("steam-signin").textContent=m.steamId?"STEAM CONNECTED":"SIGN IN WITH STEAM";
  $("settings-dialog").hidden=false;
  $("settings-status").textContent=m.directoryConnected?"Directory account connected.":"Settings loaded. Directory sign-in may be required.";
 }
});
$("search").addEventListener("input",render);$("filter").addEventListener("change",render);
$("refresh").addEventListener("click",()=>send({type:"servers.refresh"}));
$("settings").addEventListener("click",()=>send({type:"settings.get"}));
$("settings-close").addEventListener("click",()=>$("settings-dialog").hidden=true);
$("settings-cancel").addEventListener("click",()=>$("settings-dialog").hidden=true);
$("settings-save").addEventListener("click",()=>{
 const directoryUrl=$("directory-url").value.trim(),gamePath=$("game-path").value.trim();
 if(directoryUrl){try{const parsed=new URL(directoryUrl);if(parsed.protocol!=="https:"){throw new Error();}}catch{$("settings-status").textContent="Enter a valid HTTPS directory URL, or leave it blank.";return;}}
 if(gamePath.length>1024){$("settings-status").textContent="The game path is too long.";return;}
 send({type:"settings.save",directoryUrl,gamePath});
 $("settings-status").textContent="Saving launcher settings…";
});
$("steam-signin").addEventListener("click",()=>send({type:"steam.signin"}));
$("profile").addEventListener("click",event=>{event.preventDefault();send({type:"profile.open"});});
$("minimize").addEventListener("click",()=>send({type:"launcher.minimize"}));
$("close").addEventListener("click",()=>send({type:"launcher.close"}));
document.querySelector(".brand").addEventListener("pointerdown",event=>{if(event.button===0)send({type:"launcher.drag"});});
render();send({type:"servers.refresh"});send({type:"settings.get"});
})();