import { formatT } from "./i18n.js";

const DISCORD_URL = "https://discord.gg/29bndFuds3";

let snapshot = null;
let currentLang = "en";
let activeTab = "control";
let modFilter = "all";
let searchQuery = "";
let pendingDeleteMod = null;
let lastRenderKey = "";
let isActionBusy = false;
let launchCooldownUntil = 0;
let lastFilteredModIds = [];

function isLaunchCoolingDown() {
  return Date.now() < launchCooldownUntil;
}

function triggerLaunchCooldown() {
  launchCooldownUntil = Date.now() + 6000;
  lastRenderKey = "";
  if (snapshot) applySnapshot(snapshot, true);
  setTimeout(() => {
    lastRenderKey = "";
    if (snapshot) applySnapshot(snapshot, true);
  }, 6100);
}

const pendingRequests = new Map();
let reqCounter = 0;

function t(key, vars = {}) {
  return formatT(currentLang, key, vars);
}

function sendBridge(action, payload = {}, additionalObjects = null) {
  if (!window.chrome?.webview) {
    return Promise.resolve(null);
  }
  return new Promise((resolve, reject) => {
    const id = `req_${++reqCounter}`;
    pendingRequests.set(id, { resolve, reject });
    const msg = JSON.stringify({ id, action, payload });

    try {
      if (additionalObjects && additionalObjects.length > 0 && window.chrome.webview.postMessageWithAdditionalObjects) {
        window.chrome.webview.postMessageWithAdditionalObjects(msg, additionalObjects);
      } else {
        window.chrome.webview.postMessage(msg);
      }
    } catch (err) {
      pendingRequests.delete(id);
      reject(err);
    }
  });
}

window.chrome?.webview?.addEventListener("message", (event) => {
  const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
  if (!data) return;

  if (data.snapshot) {
    applySnapshot(data.snapshot);
  }

  if (data.id && pendingRequests.has(data.id)) {
    const { resolve } = pendingRequests.get(data.id);
    pendingRequests.delete(data.id);
    resolve(data);
  }
});

function showToast(message, kind = "ok") {
  if (!message) return;
  const stack = document.getElementById("toastStack");
  if (!stack) return;

  // Replace existing 'ok' toasts or identical messages so rapid clicks never pile up
  for (const existing of Array.from(stack.children)) {
    if (existing.textContent === message || (kind === "ok" && existing.classList.contains("is-ok"))) {
      existing.remove();
    }
  }

  // Cap visible stack at 2 toasts max
  while (stack.children.length >= 2) {
    stack.firstElementChild?.remove();
  }

  const el = document.createElement("div");
  el.className = `toast is-${kind}`;
  el.textContent = message;
  stack.appendChild(el);

  const durationMs = kind === "ok" ? 2200 : 3800;
  setTimeout(() => {
    el.remove();
  }, durationMs);
}

function handleActionResponse(res, silentIfCancelled = true) {
  if (!res?.result) return;
  const { success, code, message, warnings } = res.result;

  if (silentIfCancelled && code === "CANCELLED") return;

  if (!success) {
    if (code === "GAME_RUNNING") {
      showToast(t("toastGameRunningErr"), "err");
    } else if (code === "LAUNCH_COOLDOWN") {
      showToast(t("toastLaunchCooldown"), "warn");
    } else {
      showToast(message || "Operation failed.", "err");
    }
    return;
  }

  if (code !== "STATE_READY" && code !== "NOTICE_DISMISSED") {
    showToast(message, "ok");
  }

  if (Array.isArray(warnings)) {
    for (const w of warnings) {
      showToast(w, "warn");
    }
  }
}

function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return "0 MB";
  const mb = bytes / (1024 * 1024);
  if (mb >= 1024) {
    return `${(mb / 1024).toFixed(2)} GB`;
  }
  return mb >= 10 ? `${mb.toFixed(1)} MB` : `${mb.toFixed(2)} MB`;
}

function applyStaticTranslations() {
  document.documentElement.lang = currentLang;
  for (const el of document.querySelectorAll("[data-i18n]")) {
    const key = el.getAttribute("data-i18n");
    if (key) {
      el.textContent = t(key);
    }
  }

  const searchInput = document.getElementById("modSearchInput");
  if (searchInput) searchInput.placeholder = t("searchPlaceholder");

  const nameInput = document.getElementById("inputProfileName");
  if (nameInput) nameInput.placeholder = t("fieldProfileNamePlaceholder");

  const descInput = document.getElementById("inputProfileDesc");
  if (descInput) descInput.placeholder = t("fieldProfileDescPlaceholder");

  const shareInput = document.getElementById("shareImportInput");
  if (shareInput) shareInput.placeholder = t("sharePastePlaceholder");

  const updateEl = document.getElementById("updateStatusText");
  if (updateEl && !updateEl.hidden) {
    updateEl.textContent = t("updateCurrentText");
  }

  document.getElementById("btnLangTr").classList.toggle("is-active", currentLang === "tr");
  document.getElementById("btnLangEn").classList.toggle("is-active", currentLang === "en");
}

function applySnapshot(nextSnap, force = false) {
  snapshot = nextSnap;
  currentLang = nextSnap.resolvedLanguage === "tr" ? "tr" : "en";

  const signature = JSON.stringify({
    lang: currentLang,
    gameRunning: nextSnap.gameRunning,
    install: nextSnap.activeInstallation,
    detected: nextSnap.detectedInstallations,
    mods: nextSnap.modSummary,
    checksum: nextSnap.checksum,
    profiles: nextSnap.profiles,
    settings: nextSnap.settings,
    notice: nextSnap.legacyMigrationNotice
  });

  if (!force && signature === lastRenderKey) {
    return;
  }
  lastRenderKey = signature;

  applyStaticTranslations();
  renderTopStatus();
  renderBanners();
  renderMissionControl();
  renderModsTab();
  renderSettingsTab();
}

function renderTopStatus() {
  const versionEl = document.getElementById("appVersionBadge");
  if (versionEl && snapshot?.appVersion) {
    versionEl.textContent = `v${snapshot.appVersion}`;
  }

  const pChip = document.getElementById("platformChip");
  const pDot = document.getElementById("platformDot");
  const pLabel = document.getElementById("platformChipLabel");

  if (snapshot?.activeInstallation) {
    pChip.classList.remove("is-warn");
    pDot.className = "status-dot is-autumn";
    pLabel.textContent = snapshot.activeInstallation.displayLabel;
    pChip.title = snapshot.activeInstallation.installPath;
  } else {
    pChip.classList.add("is-warn");
    pDot.className = "status-dot is-red";
    pLabel.textContent = t("statusNoInstall");
    pChip.title = "";
  }

  const gChip = document.getElementById("gameRunChip");
  const gDot = document.getElementById("gameRunDot");
  const gLabel = document.getElementById("gameRunLabel");

  if (snapshot?.gameRunning) {
    gChip.classList.add("is-danger");
    gDot.className = "status-dot is-red";
    gLabel.textContent = t("statusGameRunning");
  } else {
    gChip.classList.remove("is-danger");
    gDot.className = "status-dot";
    gLabel.textContent = t("statusReady");
  }

  const totalMods = snapshot?.modSummary?.mods?.length ?? 0;
  document.getElementById("modsTabCounter").textContent = String(totalMods);
}

function renderBanners() {
  const migBanner = document.getElementById("migrationBanner");
  const migText = document.getElementById("migrationBannerText");
  if (snapshot?.legacyMigrationNotice) {
    migText.textContent = snapshot.legacyMigrationNotice;
    migBanner.hidden = false;
  } else {
    migBanner.hidden = true;
  }

  const noInstall = document.getElementById("noInstallBanner");
  noInstall.hidden = Boolean(snapshot?.activeInstallation);
}

function renderMissionControl() {
  const modeCard = document.getElementById("heroModeCard");
  const modeTitle = document.getElementById("heroModeTitle");
  const modeStats = document.getElementById("heroModeStats");
  const btnQuickToggle = document.getElementById("btnQuickToggleMode");
  const btnLaunch = document.getElementById("btnLaunchGameHero");

  const modSummary = snapshot?.modSummary;
  const hasInstall = Boolean(snapshot?.activeInstallation);
  const coolingDown = isLaunchCoolingDown();
  const locked = Boolean(snapshot?.gameRunning) || !hasInstall;

  btnQuickToggle.disabled = locked || coolingDown;
  btnLaunch.disabled = !hasInstall || Boolean(snapshot?.gameRunning) || coolingDown;
  const heroLaunchSpan = btnLaunch.querySelector("span[data-i18n]");
  if (heroLaunchSpan) {
    heroLaunchSpan.textContent = coolingDown ? t("launchingText") : t("btnLaunchGame");
  }

  const currentLaunchMode = snapshot?.settings?.launchMode || "platformUri";
  for (const apiBtn of document.querySelectorAll(".api-btn[data-launch-mode]")) {
    apiBtn.classList.toggle("is-active", apiBtn.dataset.launchMode === currentLaunchMode);
    apiBtn.disabled = coolingDown;
  }

  modeCard.classList.remove("is-modded", "is-vanilla", "is-mixed");

  if (!modSummary) {
    modeTitle.textContent = "—";
    modeStats.textContent = t("noInstallDesc");
    btnQuickToggle.textContent = t("btnQuickToggleToVanilla");
  } else {
    const mode = modSummary.workspaceMode;
    if (mode === "modded") {
      modeCard.classList.add("is-modded");
      modeTitle.textContent = t("modeModded");
      btnQuickToggle.textContent = t("btnQuickToggleToVanilla");
    } else if (mode === "vanilla") {
      modeCard.classList.add("is-vanilla");
      modeTitle.textContent = t("modeVanilla");
      btnQuickToggle.textContent = t("btnQuickToggleToModded");
    } else if (mode === "mixed") {
      modeCard.classList.add("is-mixed");
      modeTitle.textContent = t("modeMixed");
      btnQuickToggle.textContent = t("btnQuickToggleToVanilla");
    } else {
      modeTitle.textContent = t("modeClean");
      btnQuickToggle.textContent = t("btnQuickToggleToModded");
    }

    const parts = [
      t("activeModsCount", { count: modSummary.activePakCount }),
      t("stashedModsCount", { count: modSummary.stashedPakCount }),
      t("singleDisabledCount", { count: modSummary.singleDisabledCount })
    ];
    if (modSummary.totalActiveSizeBytes > 0) {
      parts.push(formatBytes(modSummary.totalActiveSizeBytes));
    }
    modeStats.textContent = parts.join(" · ");
  }

  const csTitle = document.getElementById("heroChecksumTitle");
  const btnCs = document.getElementById("btnToggleChecksum");
  const cs = snapshot?.checksum;

  if (!cs || !cs.configFound) {
    csTitle.textContent = t("checksumMissing");
    btnCs.disabled = true;
  } else {
    csTitle.textContent = cs.serverSideChecksum ? t("checksumOn") : t("checksumOff");
    btnCs.disabled = locked;
  }

  // Profiles Grid (Clean Architectural Cards without Pill Clutter)
  const grid = document.getElementById("profilesGrid");
  grid.replaceChildren();

  const profiles = snapshot?.profiles ?? [];
  const activeProfileId = snapshot?.settings?.activeProfileId;
  const totalKnownMods = modSummary?.mods?.length ?? 0;

  profiles.forEach((prof, idx) => {
    const card = document.createElement("article");
    const isActive = activeProfileId && activeProfileId.toLowerCase() === prof.id.toLowerCase();
    const isVanilla = prof.builtInType === "vanilla" || !prof.isModdedMode;

    card.className = "profile-card";
    if (isActive) card.classList.add("is-active");
    if (isVanilla) card.classList.add("is-vanilla");

    const topWrap = document.createElement("div");

    const headRow = document.createElement("div");
    headRow.className = "profile-card-head";

    const indexEl = document.createElement("span");
    indexEl.className = "profile-index";
    indexEl.textContent = String(idx + 1).padStart(2, "0");
    headRow.appendChild(indexEl);

    if (isActive) {
      const actEl = document.createElement("span");
      actEl.className = "profile-active-label";
      actEl.textContent = t("badgeActiveProfile");
      headRow.appendChild(actEl);
    }

    const title = document.createElement("h4");
    title.className = "profile-name";
    title.textContent = resolveProfileTitle(prof);

    const desc = document.createElement("p");
    desc.className = "profile-desc";
    desc.textContent = resolveProfileDesc(prof);

    topWrap.append(headRow, title, desc);

    const metaLine = document.createElement("div");
    metaLine.className = "profile-meta-line";

    let modsText;
    if (!prof.isModdedMode) {
      modsText = t("metaNoMods");
    } else if (prof.trackNewModsAsEnabled) {
      const enabledCount = Math.max(0, totalKnownMods - (prof.disabledModIds?.length ?? 0));
      modsText = t("metaAllMods", { count: enabledCount });
    } else {
      modsText = t("metaModsIncluded", { count: prof.enabledModIds?.length ?? 0 });
    }

    const csText = prof.disableChecksum ? t("metaChecksumOff") : t("metaChecksumOn");
    const modIoText = prof.enableModIo ? t("metaModIoOn") : t("metaModIoOff");
    metaLine.textContent = `${modsText}  ·  ${csText}  ·  ${modIoText}`;

    const actions = document.createElement("div");
    actions.className = "profile-actions";

    const btnApply = document.createElement("button");
    btnApply.type = "button";
    btnApply.className = "btn btn-outline btn-sm";
    btnApply.textContent = t("btnApplyProfile");
    btnApply.disabled = locked || coolingDown;
    btnApply.addEventListener("click", async () => {
      if (isActionBusy) return;
      isActionBusy = true;
      try {
        const res = await sendBridge("applyProfile", { profileId: prof.id, launchAfter: false });
        handleActionResponse(res);
      } finally {
        isActionBusy = false;
      }
    });

    const btnApplyLaunch = document.createElement("button");
    btnApplyLaunch.type = "button";
    btnApplyLaunch.className = isActive ? "btn btn-solid btn-sm" : "btn btn-autumn btn-sm";
    btnApplyLaunch.textContent = coolingDown ? t("launchingText") : `${t("btnApplyAndLaunch")} →`;
    btnApplyLaunch.disabled = locked || coolingDown;
    btnApplyLaunch.addEventListener("click", async () => {
      if (isActionBusy || isLaunchCoolingDown()) return;
      isActionBusy = true;
      try {
        const res = await sendBridge("applyProfile", { profileId: prof.id, launchAfter: true });
        if (res?.result?.success) triggerLaunchCooldown();
        handleActionResponse(res);
      } finally {
        isActionBusy = false;
      }
    });

    actions.append(btnApply, btnApplyLaunch);

    if (prof.isModdedMode) {
      const btnSaveSel = document.createElement("button");
      btnSaveSel.type = "button";
      btnSaveSel.className = "btn btn-ghost btn-sm";
      btnSaveSel.textContent = t("btnSaveSelectionToProfile");
      btnSaveSel.title = t("btnSaveSelectionHint");
      btnSaveSel.disabled = !hasInstall;
      btnSaveSel.addEventListener("click", async () => {
        const res = await sendBridge("saveProfileSelection", { profileId: prof.id });
        handleActionResponse(res);
      });
      actions.appendChild(btnSaveSel);
    }

    if (!prof.isBuiltIn) {
      const btnDel = document.createElement("button");
      btnDel.type = "button";
      btnDel.className = "btn btn-ghost btn-sm";
      btnDel.textContent = t("btnDeleteProfile");
      btnDel.addEventListener("click", async () => {
        const res = await sendBridge("deleteProfile", { profileId: prof.id });
        handleActionResponse(res);
      });
      actions.appendChild(btnDel);
    }

    card.append(topWrap, metaLine, actions);
    grid.appendChild(card);
  });
}

function resolveProfileTitle(prof) {
  if (prof.builtInType === "coop") return t("profileCoopName");
  if (prof.builtInType === "solo") return t("profileSoloName");
  if (prof.builtInType === "vanilla") return t("profileVanillaName");
  return prof.name;
}

function resolveProfileDesc(prof) {
  if (prof.builtInType === "coop") return t("profileCoopDesc");
  if (prof.builtInType === "solo") return t("profileSoloDesc");
  if (prof.builtInType === "vanilla") return t("profileVanillaDesc");
  return prof.description || "";
}

function renderModsTab() {
  const listEl = document.getElementById("modsList");
  listEl.replaceChildren();

  const hasInstall = Boolean(snapshot?.activeInstallation);
  const locked = Boolean(snapshot?.gameRunning) || !hasInstall;

  document.getElementById("btnAddMods").disabled = locked;
  document.getElementById("btnOpenModsFolder").disabled = !hasInstall;

  const mods = snapshot?.modSummary?.mods ?? [];
  const q = searchQuery.trim().toLowerCase();

  const visibleForBulk = mods.filter((m) => {
    if (modFilter === "enabled" && !m.isEnabled) return false;
    if (modFilter === "disabled" && m.isEnabled) return false;
    if (!q) return true;
    return (
      (m.displayName || "").toLowerCase().includes(q) ||
      (m.rawName || "").toLowerCase().includes(q)
    );
  });
  lastFilteredModIds = visibleForBulk.map((m) => m.id);

  const btnEnableAll = document.getElementById("btnEnableAllMods");
  if (btnEnableAll) {
    btnEnableAll.disabled = locked || !visibleForBulk.some((m) => !m.isEnabled);
  }
  const btnDisableAll = document.getElementById("btnDisableAllMods");
  if (btnDisableAll) {
    btnDisableAll.disabled = locked || !visibleForBulk.some((m) => m.isEnabled);
  }

  const filtered = mods.filter((m) => {
    if (modFilter === "enabled" && !m.isEnabled) return false;
    if (modFilter === "disabled" && m.isEnabled) return false;
    if (q) {
      const hay = `${m.displayName} ${m.rawName}`.toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  });

  if (filtered.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-box";
    const h = document.createElement("h4");
    h.textContent = t("emptyModsTitle");
    const p = document.createElement("p");
    p.textContent = t("emptyModsDesc");
    empty.append(h, p);
    listEl.appendChild(empty);
  } else {
    for (const mod of filtered) {
      const row = document.createElement("div");
      row.className = `mod-row ${mod.isEnabled ? "" : "is-off"}`;

      const info = document.createElement("div");
      info.className = "mod-info";

      const titleLine = document.createElement("div");
      titleLine.className = "mod-title-line";

      const bar = document.createElement("span");
      bar.className = "mod-state-bar";
      if (mod.bucket === "vanillaStashed") bar.classList.add("is-stashed");
      else if (!mod.isEnabled) bar.classList.add("is-off");
      titleLine.appendChild(bar);

      const nameSpan = document.createElement("span");
      nameSpan.className = "mod-name";
      nameSpan.textContent = mod.displayName;
      titleLine.appendChild(nameSpan);

      const tagParts = [];
      if (mod.pakChunkOrder !== null && mod.pakChunkOrder !== undefined) {
        tagParts.push(`#${mod.pakChunkOrder}`);
      }
      if (mod.hasIoStoreFiles) tagParts.push(t("badgeIoStore"));
      if (mod.isDirectory) tagParts.push(t("badgeDirectory"));
      if (mod.bucket === "vanillaStashed") tagParts.push(t("badgeStashedVanilla"));

      if (tagParts.length > 0) {
        const tag = document.createElement("span");
        tag.className = `mod-tag ${mod.bucket === "vanillaStashed" ? "is-autumn" : ""}`;
        tag.textContent = tagParts.join(" · ");
        titleLine.appendChild(tag);
      }

      if (mod.hasHealthWarning && mod.healthWarningCode) {
        const warnKeyMap = {
          EMPTY_FILE: "warnEmptyFile",
          MISSING_UTOC: "warnMissingUtoc",
          MISSING_UCAS: "warnMissingUcas",
          EMPTY_FOLDER: "warnEmptyFolder"
        };
        const warnKey = warnKeyMap[mod.healthWarningCode];
        if (warnKey) {
          const warnPill = document.createElement("span");
          warnPill.className = "mod-warn-pill";
          warnPill.textContent = t(warnKey);
          titleLine.appendChild(warnPill);
        }
      }

      const raw = document.createElement("div");
      raw.className = "mod-raw";
      raw.textContent = mod.rawName + (mod.files?.length > 1 ? ` (+${mod.files.length - 1})` : "");
      raw.title = mod.files?.join("\n") || mod.rawName;

      info.append(titleLine, raw);

      const controls = document.createElement("div");
      controls.className = "mod-controls";

      const sizeEl = document.createElement("span");
      sizeEl.className = "mod-size";
      sizeEl.textContent = formatBytes(mod.totalSizeBytes);

      const sw = document.createElement("input");
      sw.type = "checkbox";
      sw.setAttribute("role", "switch");
      sw.className = "arch-switch";
      sw.checked = mod.isEnabled;
      sw.disabled = locked;
      sw.setAttribute("aria-label", mod.displayName);
      sw.addEventListener("change", async () => {
        const targetState = sw.checked;
        const res = await sendBridge("toggleMod", { modId: mod.id, enable: targetState });
        handleActionResponse(res);
        if (res?.result?.success && targetState &&
            (snapshot?.modSummary?.workspaceMode === "vanilla" || snapshot?.settings?.activeProfileId === "vanilla")) {
          showToast(t("toastVanillaNotice"), "warn");
        }
      });

      const delBtn = document.createElement("button");
      delBtn.type = "button";
      delBtn.className = "icon-btn";
      delBtn.disabled = locked;
      delBtn.title = t("deleteModTooltip");
      delBtn.setAttribute("aria-label", `${t("deleteModTooltip")}: ${mod.displayName}`);
      delBtn.innerHTML = `<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>`;
      delBtn.addEventListener("click", () => {
        pendingDeleteMod = mod;
        document.getElementById("deleteModDialogBody").textContent = t("modalDeleteBody", { name: mod.displayName });
        document.getElementById("deleteModDialog").showModal();
      });

      controls.append(sizeEl, sw, delBtn);
      row.append(info, controls);
      listEl.appendChild(row);
    }
  }

  const modIoBox = document.getElementById("modIoContainer");
  const modIoList = document.getElementById("modIoList");
  const modIoEntries = snapshot?.modSummary?.modIoEntries ?? [];
  modIoList.replaceChildren();

  if (modIoEntries.length > 0) {
    modIoBox.hidden = false;
    for (const entry of modIoEntries) {
      const pill = document.createElement("div");
      pill.className = "modio-item";

      const dot = document.createElement("span");
      dot.className = `status-dot ${entry.isCurrentlyActiveOnDisk ? "" : "is-autumn"}`;

      const lbl = document.createElement("span");
      lbl.textContent = `${entry.displayName} · ${formatBytes(entry.totalSizeBytes)} · ${entry.isCurrentlyActiveOnDisk ? t("badgeActiveDisk") : t("badgeStashedVanilla")}`;

      pill.append(dot, lbl);
      modIoList.appendChild(pill);
    }
  } else {
    modIoBox.hidden = true;
  }
}

function renderSettingsTab() {
  const listEl = document.getElementById("installationsList");
  listEl.replaceChildren();

  const installs = [...(snapshot?.detectedInstallations ?? [])];
  const active = snapshot?.activeInstallation;

  if (active && !installs.some((i) => i.installPath.toLowerCase() === active.installPath.toLowerCase())) {
    installs.push(active);
  }

  for (const inst of installs) {
    const isCurrent = active && inst.installPath.toLowerCase() === active.installPath.toLowerCase();
    const item = document.createElement("div");
    item.className = `install-item ${isCurrent ? "is-active" : ""}`;

    const left = document.createElement("div");
    const head = document.createElement("strong");
    head.textContent = `${inst.displayLabel} · ${inst.isAutoDetected ? t("platformDetectedAuto") : t("platformCustomPath")}`;

    const pathEl = document.createElement("div");
    pathEl.className = "install-path";
    pathEl.textContent = inst.installPath;
    left.append(head, pathEl);

    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = isCurrent ? "btn btn-sm btn-active-badge" : "btn btn-outline btn-sm";
    btn.textContent = isCurrent ? t("btnActivePlatform") : t("btnUsePlatform");
    btn.disabled = isCurrent;
    btn.addEventListener("click", async () => {
      const res = await sendBridge("selectPlatform", { platform: inst.platform });
      handleActionResponse(res);
    });

    item.append(left, btn);
    listEl.appendChild(item);
  }

  const iniVal = document.getElementById("iniPathValue");
  if (active) {
    iniVal.textContent = active.configExists
      ? active.configIniPath
      : `${active.configIniPath} (${t("iniNotCreatedYet")})`;
  } else {
    iniVal.textContent = "—";
  }

  const btnRestoreIni = document.getElementById("btnRestoreIni");
  if (btnRestoreIni) {
    const hasBackup = Boolean(snapshot?.checksum?.hasBackup);
    const locked = Boolean(snapshot?.gameRunning) || !active;
    btnRestoreIni.hidden = !hasBackup;
    btnRestoreIni.disabled = locked || !hasBackup;
    btnRestoreIni.title = snapshot?.checksum?.backupPath || "";
  }

  const settings = snapshot?.settings;
  if (settings) {
    document.getElementById("selectLaunchMode").value = settings.launchMode || "platformUri";
    document.getElementById("chkIncludeModIo").checked = settings.includeModIoInSwitch !== false;
    document.getElementById("chkMinimizeOnLaunch").checked = Boolean(settings.minimizeOnLaunch);
  }
}

function selectTab(tabName) {
  activeTab = tabName;
  const tabs = Array.from(document.querySelectorAll('[role="tab"]'));
  const panels = Array.from(document.querySelectorAll('[role="tabpanel"]'));

  for (const tab of tabs) {
    const selected = tab.dataset.tab === tabName;
    tab.setAttribute("aria-selected", selected ? "true" : "false");
  }
  for (const panel of panels) {
    const match = panel.id === `panel-${tabName}`;
    panel.hidden = !match;
  }
}

function wireEvents() {
  const tabs = Array.from(document.querySelectorAll('[role="tab"]'));
  for (const tab of tabs) {
    tab.addEventListener("click", () => selectTab(tab.dataset.tab));
    tab.addEventListener("keydown", (e) => {
      const idx = tabs.indexOf(tab);
      let nextIdx = -1;
      if (e.key === "ArrowDown" || e.key === "ArrowRight") nextIdx = (idx + 1) % tabs.length;
      else if (e.key === "ArrowUp" || e.key === "ArrowLeft") nextIdx = (idx - 1 + tabs.length) % tabs.length;
      else if (e.key === "Home") nextIdx = 0;
      else if (e.key === "End") nextIdx = tabs.length - 1;

      if (nextIdx >= 0) {
        e.preventDefault();
        tabs[nextIdx].focus();
        selectTab(tabs[nextIdx].dataset.tab);
      }
    });
  }

  document.getElementById("btnLangTr").addEventListener("click", async () => {
    if (!window.chrome?.webview && snapshot) {
      snapshot.resolvedLanguage = "tr";
      applySnapshot(snapshot, true);
      return;
    }
    await sendBridge("setLanguage", { language: "tr" });
  });

  document.getElementById("btnLangEn").addEventListener("click", async () => {
    if (!window.chrome?.webview && snapshot) {
      snapshot.resolvedLanguage = "en";
      applySnapshot(snapshot, true);
      return;
    }
    await sendBridge("setLanguage", { language: "en" });
  });

  document.getElementById("btnDismissMigration").addEventListener("click", async () => {
    await sendBridge("dismissNotice");
  });
  document.getElementById("btnPickFolderBanner").addEventListener("click", async () => {
    const res = await sendBridge("browseGameFolder");
    handleActionResponse(res);
  });

  document.getElementById("btnQuickToggleMode").addEventListener("click", async () => {
    const isCurrentlyVanilla = snapshot?.modSummary?.workspaceMode === "vanilla" ||
                               snapshot?.modSummary?.workspaceMode === "clean";
    const res = await sendBridge("toggleWorkspace", { enableModded: isCurrentlyVanilla });
    handleActionResponse(res);
  });

  document.getElementById("btnLaunchGameHero").addEventListener("click", async () => {
    if (isActionBusy || isLaunchCoolingDown()) return;
    isActionBusy = true;
    try {
      const res = await sendBridge("launchGame");
      if (res?.result?.success) triggerLaunchCooldown();
      handleActionResponse(res);
    } finally {
      isActionBusy = false;
    }
  });

  document.getElementById("btnToggleChecksum").addEventListener("click", async () => {
    const currentServerChecksum = snapshot?.checksum?.serverSideChecksum ?? true;
    const res = await sendBridge("setChecksumDisabled", { disable: currentServerChecksum });
    handleActionResponse(res);
  });

  const newProfileDialog = document.getElementById("newProfileDialog");
  const newProfileForm = document.getElementById("newProfileForm");
  document.getElementById("btnOpenNewProfile").addEventListener("click", () => {
    newProfileForm.reset();
    document.getElementById("inputProfileChecksum").checked = true;
    document.getElementById("inputProfileModIo").checked = true;
    newProfileDialog.showModal();
  });
  document.getElementById("btnCancelNewProfile").addEventListener("click", () => {
    newProfileDialog.close("cancel");
  });

  newProfileDialog.addEventListener("close", async () => {
    const choice = newProfileDialog.returnValue;
    newProfileDialog.returnValue = "";
    if (choice !== "confirm") return;

    const name = document.getElementById("inputProfileName").value.trim();
    const description = document.getElementById("inputProfileDesc").value.trim();
    const disableChecksum = document.getElementById("inputProfileChecksum").checked;
    const enableModIo = document.getElementById("inputProfileModIo").checked;

    const res = await sendBridge("createProfile", { name, description, disableChecksum, enableModIo });
    handleActionResponse(res);
  });

  const deleteModDialog = document.getElementById("deleteModDialog");
  deleteModDialog.addEventListener("close", async () => {
    const choice = deleteModDialog.returnValue;
    deleteModDialog.returnValue = "";
    const target = pendingDeleteMod;
    pendingDeleteMod = null;

    if (choice !== "delete" || !target) return;
    const res = await sendBridge("deleteMod", { modId: target.id });
    handleActionResponse(res);
  });

  document.getElementById("modSearchInput").addEventListener("input", (e) => {
    searchQuery = e.target.value || "";
    renderModsTab();
  });

  for (const btn of document.querySelectorAll(".seg-btn")) {
    btn.addEventListener("click", () => {
      modFilter = btn.dataset.filter || "all";
      for (const b of document.querySelectorAll(".seg-btn")) {
        b.classList.toggle("is-active", b === btn);
      }
      renderModsTab();
    });
  }

  document.getElementById("btnAddMods").addEventListener("click", async () => {
    const res = await sendBridge("browseAndInstallMods");
    handleActionResponse(res);
  });

  document.getElementById("btnOpenModsFolder").addEventListener("click", async () => {
    const res = await sendBridge("openModsFolder");
    handleActionResponse(res);
  });

  document.getElementById("btnRefreshMods").addEventListener("click", async () => {
    lastRenderKey = "";
    await sendBridge("getState");
  });

  for (const apiBtn of document.querySelectorAll(".api-btn[data-launch-mode]")) {
    apiBtn.addEventListener("click", async () => {
      const launchMode = apiBtn.dataset.launchMode;
      if (!launchMode) return;
      const res = await sendBridge("setLaunchModeQuick", { launchMode });
      handleActionResponse(res);
    });
  }

  document.getElementById("btnEnableAllMods")?.addEventListener("click", async () => {
    if (lastFilteredModIds.length === 0) return;
    const res = await sendBridge("setBulkMods", { modIds: lastFilteredModIds, enable: true });
    handleActionResponse(res);
  });

  document.getElementById("btnDisableAllMods")?.addEventListener("click", async () => {
    if (lastFilteredModIds.length === 0) return;
    const res = await sendBridge("setBulkMods", { modIds: lastFilteredModIds, enable: false });
    handleActionResponse(res);
  });

  const shareDialog = document.getElementById("shareCompareDialog");
  const shareSelect = document.getElementById("shareExportSelect");
  const sharePreview = document.getElementById("shareExportPreview");
  const shareImportInput = document.getElementById("shareImportInput");
  const compareResultBox = document.getElementById("compareResultBox");
  const btnSyncCoop = document.getElementById("btnSyncCoopFromShare");

  const setExportPreviewText = (text) => {
    if (!sharePreview) return;
    if (sharePreview.tagName === "TEXTAREA" || sharePreview.tagName === "INPUT") {
      sharePreview.value = text;
    } else {
      sharePreview.textContent = text;
    }
  };

  const renderCompareList = (headEl, listEl, i18nKey, items) => {
    const arr = Array.isArray(items) ? items : [];
    if (headEl) headEl.textContent = t(i18nKey, { count: arr.length });
    if (!listEl) return;
    listEl.replaceChildren();
    if (arr.length === 0) {
      const li = document.createElement("li");
      li.className = "is-empty";
      li.textContent = t("compareNone");
      listEl.appendChild(li);
      return;
    }
    for (const item of arr) {
      const li = document.createElement("li");
      li.textContent = item;
      listEl.appendChild(li);
    }
  };

  const fetchShareExport = async (copy = false) => {
    const profileId = shareSelect?.value;
    if (!profileId) {
      setExportPreviewText("");
      return null;
    }
    const res = await sendBridge("exportProfile", { profileId, copy });
    if (!res?.result) return null;
    if (!res.result.success) {
      handleActionResponse(res);
      return null;
    }
    const shareText =
      typeof res.result.data === "string"
        ? res.result.data
        : (res.result.data?.shareText ?? res.result.data?.exportText ?? "");
    setExportPreviewText(shareText);
    return { shareText, code: res.result.code };
  };

  document.getElementById("btnOpenShareCompare")?.addEventListener("click", async () => {
    if (!shareDialog) return;
    if (shareSelect) {
      shareSelect.replaceChildren();
      const profiles = snapshot?.profiles ?? [];
      for (const prof of profiles) {
        const opt = document.createElement("option");
        opt.value = prof.id;
        opt.textContent = resolveProfileTitle(prof);
        shareSelect.appendChild(opt);
      }
      const activeId = snapshot?.settings?.activeProfileId;
      const activeProf = profiles.find((p) => p.id.toLowerCase() === (activeId || "").toLowerCase());
      const defaultId =
        (activeProf && activeProf.isModdedMode ? activeProf.id : null) ||
        profiles.find((p) => p.id.toLowerCase() === "coop")?.id ||
        profiles[0]?.id ||
        "";
      shareSelect.value = defaultId;
    }
    if (compareResultBox) compareResultBox.hidden = true;
    if (btnSyncCoop) {
      btnSyncCoop.hidden = true;
      btnSyncCoop.disabled = true;
    }
    setExportPreviewText("");
    shareDialog.showModal();
    await fetchShareExport(false);
  });

  document.getElementById("btnCloseShareDialog")?.addEventListener("click", () => {
    shareDialog?.close("close");
  });

  shareDialog?.addEventListener("close", () => {
    shareDialog.returnValue = "";
  });

  shareSelect?.addEventListener("change", () => {
    fetchShareExport(false);
  });

  document.getElementById("btnCopyShareCode")?.addEventListener("click", async () => {
    const exported = await fetchShareExport(true);
    if (!exported?.shareText) return;
    if (exported.code !== "COPIED") {
      await sendBridge("copyToClipboard", { text: exported.shareText });
    }
    showToast(t("toastCopied"), "ok");
  });

  document.getElementById("btnCompareShared")?.addEventListener("click", async () => {
    const rawText = (shareImportInput?.value || "").trim();
    const res = await sendBridge("compareSharedProfile", { rawText });
    if (!res?.result) return;
    if (!res.result.success) {
      handleActionResponse(res);
      return;
    }

    const cmp = res.result.data;
    if (!cmp) return;

    renderCompareList(
      document.getElementById("compareMatchHead"),
      document.getElementById("compareMatchList"),
      "compareMatchLabel",
      cmp.matchingMods
    );
    renderCompareList(
      document.getElementById("compareMissingHead"),
      document.getElementById("compareMissingList"),
      "compareMissingLabel",
      cmp.missingLocalMods
    );
    renderCompareList(
      document.getElementById("compareExtraHead"),
      document.getElementById("compareExtraList"),
      "compareExtraLabel",
      cmp.extraLocalEnabledMods
    );

    if (compareResultBox) compareResultBox.hidden = false;
    if (btnSyncCoop) {
      const matchedCount = Array.isArray(cmp.matchedLocalModIds) ? cmp.matchedLocalModIds.length : 0;
      btnSyncCoop.hidden = false;
      btnSyncCoop.disabled = Boolean(snapshot?.gameRunning) || matchedCount === 0;
    }
  });

  document.getElementById("btnSyncCoopFromShare")?.addEventListener("click", async () => {
    const rawText = (shareImportInput?.value || "").trim();
    if (!rawText) return;
    const res = await sendBridge("applySharedToCoop", { rawText });
    handleActionResponse(res);
    if (res?.result?.success) {
      shareDialog?.close("synced");
    }
  });

  document.getElementById("btnBrowseFolderSettings").addEventListener("click", async () => {
    const res = await sendBridge("browseGameFolder");
    handleActionResponse(res);
  });

  document.getElementById("btnOpenIniFolder").addEventListener("click", async () => {
    const res = await sendBridge("openIniFolder");
    handleActionResponse(res);
  });

  document.getElementById("btnRestoreIni")?.addEventListener("click", async () => {
    const res = await sendBridge("restoreIniBackup");
    handleActionResponse(res);
  });

  document.getElementById("btnCheckUpdates")?.addEventListener("click", () => {
    const statusEl = document.getElementById("updateStatusText");
    if (statusEl) {
      statusEl.hidden = false;
      statusEl.textContent = t("updateCurrentText");
    }
  });

  document.getElementById("btnSavePreferences").addEventListener("click", async () => {
    const launchMode = document.getElementById("selectLaunchMode").value;
    const includeModIoInSwitch = document.getElementById("chkIncludeModIo").checked;
    const minimizeOnLaunch = document.getElementById("chkMinimizeOnLaunch").checked;
    const res = await sendBridge("updatePreferences", {
      launchMode,
      includeModIoInSwitch,
      minimizeOnLaunch
    });
    handleActionResponse(res);
  });

  const openDiscord = () => sendBridge("openExternalUrl", { url: DISCORD_URL });
  document.getElementById("btnJoinDiscordSettings").addEventListener("click", openDiscord);
  document.getElementById("btnJoinDiscordFooter").addEventListener("click", openDiscord);

  let dragDepth = 0;
  const dropOverlay = document.getElementById("dropOverlay");

  window.addEventListener("dragenter", (e) => {
    if (!e.dataTransfer?.types?.includes("Files")) return;
    e.preventDefault();
    dragDepth++;
    dropOverlay.hidden = false;
  });

  window.addEventListener("dragover", (e) => {
    if (!e.dataTransfer?.types?.includes("Files")) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = "copy";
  });

  window.addEventListener("dragleave", () => {
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) {
      dropOverlay.hidden = true;
    }
  });

  window.addEventListener("drop", async (e) => {
    e.preventDefault();
    dragDepth = 0;
    dropOverlay.hidden = true;

    const files = Array.from(e.dataTransfer?.files || []);
    if (files.length === 0) return;

    const res = await sendBridge("filesDropped", {}, files);
    handleActionResponse(res);
  });
}

async function init() {
  wireEvents();

  if (!window.chrome?.webview) {
    // Browser static design preview fallback (never runs inside WebView2 desktop app)
    applySnapshot({
      appVersion: "1.0.0",
      resolvedLanguage: "tr",
      gameRunning: false,
      activeInstallation: { displayLabel: "Epic Games", installPath: "C:\\Games\\Epic Games\\ReadyOrNot", configExists: true, configIniPath: "C:\\Users\\...\\GameUserSettings.ini", platform: "epic", isAutoDetected: true },
      detectedInstallations: [{ displayLabel: "Epic Games", installPath: "C:\\Games\\Epic Games\\ReadyOrNot", configExists: true, platform: "epic", isAutoDetected: true }],
      checksum: { configFound: true, serverSideChecksum: true },
      modSummary: {
        workspaceMode: "vanilla",
        activePakCount: 0,
        stashedPakCount: 23,
        singleDisabledCount: 6,
        totalActiveSizeBytes: 0,
        mods: [
          { id: "1", displayName: "No Crack AI", rawName: "pakchunk99-Mods_No_Crack_AI_P.pak", bucket: "vanillaStashed", isEnabled: true, totalSizeBytes: 4200000, pakChunkOrder: 99, hasIoStoreFiles: true },
          { id: "2", displayName: "Ricos Immersive TOC V3", rawName: "pakchunk99-RicosImmersiveTOC_V3.pak", bucket: "vanillaStashed", isEnabled: true, totalSizeBytes: 18500000, pakChunkOrder: 99 },
          { id: "3", displayName: "Restoration AIO BP", rawName: "pakchunk97-Restoration_AIO_BP_P.pak", bucket: "singleDisabled", isEnabled: false, totalSizeBytes: 64100000, pakChunkOrder: 97 }
        ],
        modIoEntries: []
      },
      profiles: [
        { id: "coop", name: "Co-Op", builtInType: "coop", isBuiltIn: true, isModdedMode: true, disableChecksum: true, enableModIo: true, enabledModIds: new Array(23).fill("x") },
        { id: "solo", name: "Solo", builtInType: "solo", isBuiltIn: true, isModdedMode: true, disableChecksum: true, enableModIo: true, trackNewModsAsEnabled: true, disabledModIds: [] },
        { id: "vanilla", name: "Vanilla", builtInType: "vanilla", isBuiltIn: true, isModdedMode: false, disableChecksum: false, enableModIo: false }
      ],
      settings: { activeProfileId: "vanilla", launchMode: "platformUri", includeModIoInSwitch: true }
    });
    return;
  }

  await sendBridge("getState");

  setInterval(() => {
    if (!document.hidden) {
      sendBridge("getState").catch(() => {});
    }
  }, 4000);
}

init();
