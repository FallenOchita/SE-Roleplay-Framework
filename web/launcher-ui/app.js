(() => {
"use strict";
const state={servers:[],favorites:new Set(),history:[],last:null,profile:null};
let profileRequested=false;
let settingsRequested=false;
const $=id=>document.getElementById(id), storageKey="serpf.community-dashboard.v2";
try{const saved=JSON.parse(localStorage.getItem(storageKey)||"{}");if(Array.isArray(saved.favorites))saved.favorites.filter(x=>typeof x==="string").forEach(x=>state.favorites.add(x));if(Array.isArray(saved.history))state.history=saved.history.filter(x=>typeof x==="string").slice(0,8);if(typeof saved.last==="string")state.last=saved.last;}catch{}
function save(){try{localStorage.setItem(storageKey,JSON.stringify({favorites:[...state.favorites],history:state.history,last:state.last}));}catch{}}
function online(s){return String(s.status||"").toLowerCase()==="online";}
function send(message){const bridge=window.chrome&&window.chrome.webview;if(!bridge){$("status").textContent="Desktop launcher connection is unavailable.";return;}bridge.postMessage({version:1,...message});}
function avatar(name){return String(name||"S").trim().slice(0,1).toUpperCase();}
function makeCompact(s){const row=document.createElement("div");row.className="compact-item";const icon=document.createElement("span");icon.className="server-avatar";icon.textContent=avatar(s.name);const label=document.createElement("span");label.textContent=s.name||"Community";row.append(icon,label);return row;}
function serverButton(s,label,cls){const b=document.createElement("button");b.type="button";b.textContent=label;b.className=cls||"";b.disabled=!online(s)||!s.id;b.addEventListener("click",()=>{state.last=s.id;state.history=[s.id,...state.history.filter(id=>id!==s.id)].slice(0,8);save();renderActivity();send({type:"server.connect",serverId:String(s.id)});});return b;}
function favoriteButton(s){const b=document.createElement("button");b.type="button";b.className="favorite";b.textContent=state.favorites.has(s.id)?"★ Saved":"♡ Favorite";b.setAttribute("aria-pressed",String(state.favorites.has(s.id)));b.addEventListener("click",()=>{state.favorites.has(s.id)?state.favorites.delete(s.id):state.favorites.add(s.id);save();render();});return b;}
function card(s,featured=false){const el=document.createElement("article");el.className=featured?"featured-card":"server-card";const top=document.createElement("div");top.className="card-top";const h=document.createElement("h3");h.textContent=s.name||"Unnamed community";const badge=document.createElement("span");badge.className=online(s)?"status online":"status offline";badge.textContent=s.status||"Unknown";top.append(h,badge);const desc=document.createElement("p");desc.className="description";desc.textContent=s.description||"No description provided.";const meta=document.createElement("p");meta.className=featured?"featured-meta":"details";meta.textContent=[s.region,Number.isFinite(s.players)?s.players+(Number.isFinite(s.maxPlayers)?" / "+s.maxPlayers:"")+" players":"Player count unavailable"].filter(Boolean).join(" · ");const actions=document.createElement("div");actions.className="card-actions";actions.append(favoriteButton(s),serverButton(s,"Connect"));el.append(top,desc,meta,actions);return el;}
function renderActivity(){const last=$("last-connected");last.replaceChildren();const recent=state.servers.find(s=>s.id===state.last);if(recent){const row=document.createElement("div");row.className="activity-server";const icon=document.createElement("span");icon.className="server-avatar";icon.textContent=avatar(recent.name);const info=document.createElement("div");const name=document.createElement("strong");name.textContent=recent.name;const detail=document.createElement("small");detail.textContent=recent.region||"Community server";info.append(name,detail);row.append(icon,info);last.append(row);const actions=document.createElement("div");actions.className="activity-actions";actions.append(serverButton(recent,"CONNECT AGAIN","connect-button"));last.append(actions);}else{const p=document.createElement("p");p.className="muted";p.textContent=state.last?"Your last server is not currently in the directory.":"You haven't joined a community yet.";last.append(p);}
const fav=$("favorite-preview");fav.replaceChildren();const favorites=state.servers.filter(s=>state.favorites.has(s.id)).slice(0,3);if(!favorites.length){const p=document.createElement("p");p.className="muted";p.textContent="Your saved communities appear here.";fav.append(p);}else favorites.forEach(s=>fav.append(makeCompact(s)));
const hist=$("history");hist.replaceChildren();const history=state.history.map(id=>state.servers.find(s=>s.id===id)).filter(Boolean).slice(0,5);if(!history.length){const p=document.createElement("p");p.className="muted";p.textContent="Your recently joined servers will appear here.";hist.append(p);}else history.forEach(s=>hist.append(makeCompact(s)));
}
function showProfile(){ $("home").hidden=true; $("profile-page").hidden=false; window.scrollTo(0,0); }
function showCommunity(){ $("profile-page").hidden=true; $("home").hidden=false; window.scrollTo(0,0); }
function render(){const term=$("search").value.trim().toLowerCase(),mode=$("filter").value;const rows=state.servers.filter(s=>{const hay=[s.name,s.description,s.region,s.address].filter(Boolean).join(" ").toLowerCase();return(!term||hay.includes(term))&&(mode!=="online"||online(s))&&(mode!=="offline"||!online(s))&&(mode!=="favorites"||state.favorites.has(s.id));}).sort((a,b)=>Number(online(b))-Number(online(a))||String(a.name||"").localeCompare(String(b.name||"")));$("result-count").textContent=rows.length+" of "+state.servers.length+" communities";const list=$("servers-list");list.replaceChildren();if(!rows.length){const p=document.createElement("p");p.className="empty";p.textContent=state.servers.length?"No communities match these filters.":"No servers received yet. Configure the community directory in launcher Settings.";list.append(p);}else rows.forEach(s=>list.append(card(s)));
const featured=$("featured");featured.replaceChildren();const top=state.servers.slice().sort((a,b)=>Number(online(b))-Number(online(a))||Number(b.players||0)-Number(a.players||0)).slice(0,3);$("featured-count").textContent=top.length?top.length+" communities":"No featured servers yet";if(!top.length){const p=document.createElement("p");p.className="empty";p.textContent="Featured communities will appear when the directory publishes servers.";featured.append(p);}else top.forEach(s=>featured.append(card(s,true)));renderActivity();}
const bridge=window.chrome&&window.chrome.webview;
if(bridge)bridge.addEventListener("message",event=>{
 const m=event.data;if(!m||m.version!==1)return;
 if(m.type==="servers.updated"&&Array.isArray(m.servers)){
  state.servers=m.servers.filter(s=>s&&typeof s.id==="string"&&typeof s.name==="string");
  $("status").textContent=state.servers.length+" communities received from the launcher directory.";
  $("connection-state").textContent="DIRECTORY CONNECTED";
  $("directory-status").textContent=state.servers.length+" communities currently published.";
  render();
 }else if(m.type==="profile.updated"){
  state.profile=m;
  $("profile-steam-id").textContent=m.steamId||"Not connected";
  $("profile-avatar").textContent=avatar(m.displayName||"P");
  $("profile-card-name").textContent=(m.displayName||"COMMUNITY PLAYER").toUpperCase();
  $("profile-display-name").value=m.displayName||"";
  $("profile-biography").value=m.biography||"";
  $("profile-account-state").textContent=m.authenticated?"DIRECTORY ACCOUNT CONNECTED":"SIGN IN WITH STEAM TO EDIT YOUR PROFILE";
  $("profile-status").textContent=m.status||(m.authenticated?"Profile loaded.":"Connect your Steam account and directory to create your player profile.");
  $("profile-save").disabled=!m.authenticated;
  if(profileRequested){profileRequested=false;showProfile();}
 }else if(m.type==="profile.error"){
  $("profile-status").textContent=m.message||"Could not load player profile.";
  $("profile-save").disabled=!(state.profile&&state.profile.authenticated);
  if(profileRequested)showProfile();
  profileRequested=false;
 }else if(m.type==="settings.updated"){
  $("directory-url").value=typeof m.directoryUrl==="string"?m.directoryUrl:"";
  $("game-path").value=typeof m.gamePath==="string"?m.gamePath:"";
  $("steam-account").textContent=m.steamId?"STEAM "+m.steamId:"";
  $("steam-signin").textContent=m.steamId?"STEAM CONNECTED":"SIGN IN WITH STEAM";
  $("settings-status").textContent=m.status||(m.directoryConnected?"Directory account connected.":"Settings loaded.");
  if(settingsRequested){settingsRequested=false;$("settings-dialog").hidden=false;}
 }
});
$("search").addEventListener("input",render);$("filter").addEventListener("change",render);
$("refresh").addEventListener("click",()=>send({type:"servers.refresh"}));
$("settings").addEventListener("click",()=>{settingsRequested=true;send({type:"settings.get"});});
$("settings-close").addEventListener("click",()=>$("settings-dialog").hidden=true);
$("settings-cancel").addEventListener("click",()=>$("settings-dialog").hidden=true);
$("settings-save").addEventListener("click",()=>{
 const directoryUrl=$("directory-url").value.trim(),gamePath=$("game-path").value.trim();
 if(directoryUrl){try{const parsed=new URL(directoryUrl);if(parsed.protocol!=="https:")throw new Error();}catch{$("settings-status").textContent="Enter a valid HTTPS directory URL, or leave it blank.";return;}}
 if(gamePath.length>1024){$("settings-status").textContent="The game path is too long.";return;}
 send({type:"settings.save",directoryUrl,gamePath});
 $("settings-status").textContent="Saving launcher settings…";
});
$("steam-signin").addEventListener("click",()=>send({type:"steam.signin"}));
$("profile").addEventListener("click",event=>{event.preventDefault();profileRequested=true;send({type:"profile.get"});});
$("community-home").addEventListener("click",showCommunity);
document.querySelector("nav a[href=\"#home\"]").addEventListener("click",event=>{event.preventDefault();showCommunity();});
$("profile-save").addEventListener("click",()=>{
 const displayName=$("profile-display-name").value.trim(),biography=$("profile-biography").value.trim();
 if(displayName.length>40){$("profile-status").textContent="Display name must be 40 characters or fewer.";return;}
 if(biography.length>500){$("profile-status").textContent="Biography must be 500 characters or fewer.";return;}
 $("profile-save").disabled=true;$("profile-status").textContent="Saving player profile…";
 send({type:"profile.save",displayName,biography});
});
$("minimize").addEventListener("click",()=>send({type:"launcher.minimize"}));
$("close").addEventListener("click",()=>send({type:"launcher.close"}));
document.querySelector(".brand").addEventListener("pointerdown",event=>{if(event.button===0)send({type:"launcher.drag"});});
render();send({type:"servers.refresh"});send({type:"settings.get"});
})();