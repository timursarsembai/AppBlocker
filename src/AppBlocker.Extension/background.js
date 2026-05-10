// AppBlocker Chrome Extension - Service Worker (Manifest V3)
const NATIVE_HOST = "com.appblocker.bridge";
const CONFIG_POLL_INTERVAL = 3000;

let nativePort = null;

function updateDNRRules(blockedPatterns, isSessionActive) {
  const ruleIds = Array.from({length: 100}, (_, i) => i + 1); // 1 to 100
  
  if (!isSessionActive || !blockedPatterns || blockedPatterns.length === 0) {
    chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds: ruleIds,
      addRules: []
    });
    return;
  }

  const extensionId = chrome.runtime.id;
  const newRules = [];

  blockedPatterns.forEach((pattern, index) => {
    let clean = pattern.toLowerCase()
      .replace(/^https?:\/\//, "")
      .replace(/^www\./, "")
      .replace(/\/$/, "");

    let regex = "";
    if (clean.includes("/")) {
      regex = "^https?://(www\\\\.)?" + clean.replace(/\./g, "\\\\.").replace(/\//g, "\\\\/") + ".*";
    } else {
      regex = "^https?://(www\\\\.)?" + clean.replace(/\./g, "\\\\.") + "(/.*)?$";
    }

    newRules.push({
      id: index + 1,
      priority: 1,
      action: {
        type: "redirect",
        redirect: {
          regexSubstitution: "chrome-extension://" + extensionId + "/block.html?url=\\\\0"
        }
      },
      condition: {
        regexFilter: regex,
        resourceTypes: ["main_frame"]
      }
    });
  });

  chrome.declarativeNetRequest.updateDynamicRules({
    removeRuleIds: ruleIds,
    addRules: newRules
  });
}

function handleConfig(msg) {
  const sessionEnd = msg.sessionEnd || null;
  const sessionMode = msg.mode || "None";
  let isSessionActive = sessionMode !== "None" && sessionEnd != null;

  if (sessionEnd) {
    if (Date.now() >= new Date(sessionEnd).getTime()) {
      isSessionActive = false;
    }
  }

  chrome.storage.local.set({
    blockedPatterns: msg.blocked || [],
    dopamineHabits: msg.habits || [],
    sessionEnd,
    sessionMode,
    isSessionActive
  });

  updateDNRRules(msg.blocked || [], isSessionActive);
}

function connectNative() {
  if (nativePort) return;
  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST);
    nativePort.onMessage.addListener(handleConfig);
    nativePort.onDisconnect.addListener(() => {
      nativePort = null;
    });
    nativePort.postMessage({ action: "getConfig" });
  } catch (e) {
    nativePort = null;
  }
}

function pollConfig() {
  if (nativePort) {
    try {
      nativePort.postMessage({ action: "getConfig" });
    } catch {
      nativePort = null;
      connectNative();
    }
  } else {
    connectNative();
  }
}

connectNative();
setInterval(pollConfig, CONFIG_POLL_INTERVAL);

// Initial fallback setup
chrome.storage.local.get(["blockedPatterns", "isSessionActive"], (data) => {
  updateDNRRules(data.blockedPatterns || [], data.isSessionActive || false);
});