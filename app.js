/* =====================================================================
   FH6 Tuning Calculator — UI controller
   Reads inputs (live), runs the engine, renders cards / compare table.
   Engine works in imperial; we convert metric in/out here.
   ===================================================================== */
(function () {
  "use strict";
  const { GOALS, GOAL_META, compute, validate } = window.TUNING;

  /* ---------- unit handling ---------- */
  // factor to convert a METRIC value to IMPERIAL (multiply); divide to go back.
  // aero force: 1 kgf = 2.2046226 lbf (same numeric factor as mass).
  const M2I = { weight: 2.2046226, torque: 0.7375621, ride: 0.3937008, spring: 55.99741, aero: 2.2046226, speed: 0.6213712 };
  const UNIT_LABEL = {
    imperial: { weight: "(lb)", torque: "(lb-ft)", ride: "(in)", spring: "(lb/in)", power: "(hp)", aero: "(lbf)", speed: "(mph)" },
    metric:   { weight: "(kg)", torque: "(Nm)",    ride: "(cm)", spring: "(kgf/mm)", power: "(hp)", aero: "(kgf)", speed: "(km/h)" },
  };
  const FIELD_DIM = {
    weight: "weight", torque: "torque",
    rideHeightMin: "ride", rideHeightMax: "ride",
    springRateMin: "spring", springRateMax: "spring",
    aeroFrontMin: "aero", aeroFrontMax: "aero", aeroRearMin: "aero", aeroRearMax: "aero",
    tireDiameter: "ride", targetTopSpeed: "speed",
  };
  let units = "imperial";

  const $ = (id) => document.getElementById(id);
  const num = (id) => parseFloat($(id).value) || 0;

  function toImp(dim, v) { return units === "metric" ? v * M2I[dim] : v; }
  function fromImp(dim, v) { return units === "metric" ? v / M2I[dim] : v; }

  // pretty number: trims trailing zeros, keeps up to `dp` decimals
  function nf(v, dp = 1) {
    if (v === null || v === undefined || isNaN(v)) return "—";
    const s = Number(v).toFixed(dp);
    return s.replace(/\.0+$/, "").replace(/(\.\d*?)0+$/, "$1");
  }
  const springDisp = (vImp) => nf(fromImp("spring", vImp), units === "metric" ? 2 : 0);
  const rideDisp = (vImp) => nf(fromImp("ride", vImp), 1);
  const aeroDisp = (vImp) => nf(fromImp("aero", vImp), 0) + (units === "metric" ? " kgf" : " lbf");
  const speedDisp = (mphImp) => nf(fromImp("speed", mphImp), 0) + (units === "metric" ? " km/h" : " mph");
  // optional single numeric field → imperial number (via dim) or null when blank/invalid
  function optField(id, dim) {
    const s = $(id).value.trim();
    if (s === "") return null;
    const v = parseFloat(s);
    if (!isFinite(v)) return null;
    return dim ? toImp(dim, v) : v;
  }
  // format an aero end: lbf (with % in parens) if a range was given, else % of slider, else the absent label
  const aeroVal = (pct, lbfImp, absent) => pct == null ? absent : (lbfImp != null ? `${aeroDisp(lbfImp)} (${pct}%)` : `${pct}% of range`);

  // optional lbf range from a min/max field pair → [impMin, impMax] or [null, null] if blank/invalid
  function optAeroRange(idMin, idMax) {
    const a = $(idMin).value.trim(), b = $(idMax).value.trim();
    if (a === "" || b === "") return [null, null];
    const lo = parseFloat(a), hi = parseFloat(b);
    if (!isFinite(lo) || !isFinite(hi)) return [null, null];
    return [toImp("aero", lo), toImp("aero", hi)];
  }

  /* ---------- read all inputs into engine-ready (imperial) object ---------- */
  function readInputs() {
    return {
      drivetrain: $("drivetrain").value,
      engineLocation: $("engineLocation").value,
      powertrain: $("powertrain").value,
      piClass: $("piClass").value,
      power: num("power"),
      torque: toImp("torque", num("torque")),
      weight: toImp("weight", num("weight")),
      frontWeightPct: num("frontWeight"),
      gears: Math.round(num("gears")),
      tireCompound: $("tireCompound").value,
      suspensionType: $("suspensionType").value,
      // aero kit: None | Front (splitter only) | Rear (wing only) | Full
      hasFrontAero: (function () { const k = $("aeroKit").value; return k === "Front" || k === "Full"; })(),
      hasRearAero: (function () { const k = $("aeroKit").value; return k === "Rear" || k === "Full"; })(),
      aeroInstalled: $("aeroKit").value !== "None", // any wing -> secondary downforce effects apply
      rideHeightMin: toImp("ride", num("rideHeightMin")),
      rideHeightMax: toImp("ride", num("rideHeightMax")),
      springRateMin: toImp("spring", num("springRateMin")),
      springRateMax: toImp("spring", num("springRateMax")),
      // optional downforce ranges (imperial lbf, or null = show % of slider)
      aeroFront: optAeroRange("aeroFrontMin", "aeroFrontMax"),
      aeroRear: optAeroRange("aeroRearMin", "aeroRearMax"),
      // optional gearing physics (null = HP heuristic). tire Ø in inches, target speed in mph.
      redlineRpm: optField("redlineRpm"),
      tireDiameter: optField("tireDiameter", "ride"),
      targetTopSpeed: optField("targetTopSpeed", "speed"),
      // handling bias: −5 (understeer) … 0 (neutral) … +5 (oversteer); 0 = pure baseline
      handlingBias: num("handlingBias"),
    };
  }

  /* ---------- handling-bias slider label ---------- */
  function updateBiasLabel() {
    const b = num("handlingBias");
    const el = $("biasValue");
    el.classList.remove("under", "over");
    if (b === 0) { el.textContent = "Neutral (0)"; return; }
    const dir = b > 0 ? "Oversteer" : "Understeer";
    el.classList.add(b > 0 ? "over" : "under");
    el.textContent = `${dir} (${b > 0 ? "+" : ""}${nf(b, 1)})`;
  }

  /* ---------- card definitions (single-goal view) ---------- */
  function gearRows(t) {
    const g = t.gearing;
    const rows = [{ k: "Final drive", v: nf(g.final, 2) }];
    if (g.singleSpeed) {
      rows.push({ k: "Gearbox", v: "Single-speed (EV)" });
    } else {
      g.ratios.forEach((r, idx) => rows.push({
        k: `Gear ${idx + 1}`,
        v: nf(r, 2) + (g.speeds ? `  ·  ${speedDisp(g.speeds[idx])}` : ""),
        sub: true,
      }));
    }
    if (g.topSpeed != null) rows.push({ k: "Top speed @ redline", v: speedDisp(g.topSpeed) });
    return rows;
  }
  function diffRows(t) {
    const dd = t.differential;
    if (dd.driveline === "AWD") {
      return [
        { k: "Rear accel", v: dd.accel + "%" }, { k: "Rear decel", v: dd.decel + "%" },
        { k: "Front accel", v: dd.frontAccel + "%" }, { k: "Front decel", v: dd.frontDecel + "%" },
        { k: "Center (→rear)", v: dd.centerRear + "%" },
      ];
    }
    return [{ k: dd.driveline + " accel", v: dd.accel + "%" }, { k: dd.driveline + " decel", v: dd.decel + "%" }];
  }

  const CARDS = [
    { id: "tires", title: "Tires", icon: "🛞", rows: (t) => [
        { k: "Front pressure", v: nf(t.tires.front, 1) + " psi" }, { k: "Rear pressure", v: nf(t.tires.rear, 1) + " psi" }] },
    { id: "gearing", title: "Gearing", icon: "⚙️", rows: gearRows },
    { id: "alignment", title: "Alignment", icon: "📐", rows: (t) => [
        { k: "Camber front", v: nf(t.alignment.camberF, 1) + "°" }, { k: "Camber rear", v: nf(t.alignment.camberR, 1) + "°" },
        { k: "Toe front", v: nf(t.alignment.toeF, 1) + "°" }, { k: "Toe rear", v: nf(t.alignment.toeR, 1) + "°" },
        { k: "Caster", v: nf(t.alignment.caster, 1) + "°" }] },
    { id: "arb", title: "Anti-Roll Bars", icon: "🌀", rows: (t) => [
        { k: "Front", v: nf(t.arb.front, 0) }, { k: "Rear", v: nf(t.arb.rear, 0) }] },
    { id: "springs", title: "Springs & Ride Height", icon: "🔧", rows: (t) => [
        { k: "Front rate", v: springDisp(t.springs.front) + " " + (units === "metric" ? "kgf/mm" : "lb/in") },
        { k: "Rear rate", v: springDisp(t.springs.rear) + " " + (units === "metric" ? "kgf/mm" : "lb/in") },
        { k: "Ride height F", v: rideDisp(t.springs.rideF) + (units === "metric" ? " cm" : " in") },
        { k: "Ride height R", v: rideDisp(t.springs.rideR) + (units === "metric" ? " cm" : " in") }] },
    { id: "damping", title: "Damping", icon: "〽️", rows: (t) => [
        { k: "Rebound front", v: nf(t.damping.reboundF, 1) }, { k: "Rebound rear", v: nf(t.damping.reboundR, 1) },
        { k: "Bump front", v: nf(t.damping.bumpF, 1) }, { k: "Bump rear", v: nf(t.damping.bumpR, 1) }] },
    { id: "aero", title: "Aero", icon: "🪽", rows: (t) => !t.aero.applicable
        ? [{ k: "Aero", v: "Not installed" }]
        : [
            { k: "Front downforce", v: aeroVal(t.aero.front, t.aero.frontLbf, "— (no splitter)") },
            { k: "Rear downforce", v: aeroVal(t.aero.rear, t.aero.rearLbf, "— (no wing)") },
          ] },
    { id: "braking", title: "Braking", icon: "🛑", rows: (t) => [
        { k: "Balance (→front)", v: t.braking.balance + "%" }, { k: "Pressure", v: t.braking.pressure + "%" }] },
    { id: "differential", title: "Differential", icon: "🔩", rows: diffRows },
  ];

  /* ---------- render: single-goal cards ---------- */
  function renderCards(t) {
    $("summaryStrip").innerHTML = t.summary
      .map((c) => `<span class="chip">${c.k}: <b>${c.v}</b></span>`).join("");

    $("output").innerHTML = CARDS.map((c) => {
      const why = t[c.id].why;
      const rows = c.rows(t).map((row) =>
        `<div class="row ${row.sub ? "sub" : ""}"><span class="k">${row.k}</span><span class="v">${row.v}</span></div>`
      ).join("");
      const whyHtml = why ? `
        <details class="why">
          <summary>Why &amp; formula</summary>
          <div class="body">${why.text}${why.formula && why.formula !== "—"
            ? `<span class="formula">${escapeHtml(why.formula)}</span>` : ""}</div>
        </details>` : "";
      return `<div class="card"><h3><span class="ico">${c.icon}</span>${c.title}</h3>
        <div class="rows">${rows}</div>${whyHtml}</div>`;
    }).join("");
  }

  /* ---------- render: compare table (all goals) ---------- */
  function compareRowDefs(input) {
    const n = input.gears;
    const ev = input.powertrain === "EV";
    const aeroFrontLbf = input.hasFrontAero && input.aeroFront[0] != null;
    const aeroRearLbf = input.hasRearAero && input.aeroRear[0] != null;
    const defs = [
      { group: "Tires" },
      { label: "Front psi", get: (t) => nf(t.tires.front, 1) },
      { label: "Rear psi", get: (t) => nf(t.tires.rear, 1) },
      { group: "Gearing" },
      { label: "Final drive", get: (t) => nf(t.gearing.final, 2) },
    ];
    if (!ev) for (let g = 0; g < n; g++) defs.push({ label: `Gear ${g + 1}`, get: (t) => t.gearing.ratios[g] != null ? nf(t.gearing.ratios[g], 2) : "—" });
    if (input.redlineRpm && input.tireDiameter) defs.push({ label: `Top speed (${units === "metric" ? "km/h" : "mph"})`, get: (t) => t.gearing.topSpeed != null ? nf(fromImp("speed", t.gearing.topSpeed), 0) : "—" });
    defs.push(
      { group: "Alignment" },
      { label: "Camber F", get: (t) => nf(t.alignment.camberF, 1) + "°" },
      { label: "Camber R", get: (t) => nf(t.alignment.camberR, 1) + "°" },
      { label: "Toe F", get: (t) => nf(t.alignment.toeF, 1) + "°" },
      { label: "Toe R", get: (t) => nf(t.alignment.toeR, 1) + "°" },
      { label: "Caster", get: (t) => nf(t.alignment.caster, 1) + "°" },
      { group: "Anti-Roll Bars" },
      { label: "ARB Front", get: (t) => nf(t.arb.front, 0) },
      { label: "ARB Rear", get: (t) => nf(t.arb.rear, 0) },
      { group: `Springs (${units === "metric" ? "kgf/mm" : "lb/in"})` },
      { label: "Front rate", get: (t) => springDisp(t.springs.front) },
      { label: "Rear rate", get: (t) => springDisp(t.springs.rear) },
      { label: `Ride F (${units === "metric" ? "cm" : "in"})`, get: (t) => rideDisp(t.springs.rideF) },
      { label: `Ride R (${units === "metric" ? "cm" : "in"})`, get: (t) => rideDisp(t.springs.rideR) },
      { group: "Damping" },
      { label: "Rebound F", get: (t) => nf(t.damping.reboundF, 1) },
      { label: "Rebound R", get: (t) => nf(t.damping.reboundR, 1) },
      { label: "Bump F", get: (t) => nf(t.damping.bumpF, 1) },
      { label: "Bump R", get: (t) => nf(t.damping.bumpR, 1) },
      { group: "Aero" },
      { label: aeroFrontLbf ? `Front DF (${units === "metric" ? "kgf" : "lbf"})` : "Front DF",
        get: (t) => (!t.aero.applicable || t.aero.front == null) ? "—" : (aeroFrontLbf && t.aero.frontLbf != null ? nf(fromImp("aero", t.aero.frontLbf), 0) : t.aero.front + "%") },
      { label: aeroRearLbf ? `Rear DF (${units === "metric" ? "kgf" : "lbf"})` : "Rear DF",
        get: (t) => (!t.aero.applicable || t.aero.rear == null) ? "—" : (aeroRearLbf && t.aero.rearLbf != null ? nf(fromImp("aero", t.aero.rearLbf), 0) : t.aero.rear + "%") },
      { group: "Braking" },
      { label: "Balance", get: (t) => t.braking.balance + "%" },
      { label: "Pressure", get: (t) => t.braking.pressure + "%" },
      { group: "Differential" },
      { label: "Accel lock", get: (t) => t.differential.accel + "%" },
      { label: "Decel lock", get: (t) => t.differential.decel + "%" }
    );
    if (input.drivetrain === "AWD") defs.push(
      { label: "Front accel", get: (t) => (t.differential.frontAccel ?? "—") + "%" },
      { label: "Front decel", get: (t) => (t.differential.frontDecel ?? "—") + "%" },
      { label: "Center→rear", get: (t) => (t.differential.centerRear ?? "—") + "%" }
    );
    return defs;
  }

  function renderCompare(input) {
    const tunes = GOALS.map((g) => compute(input, g));
    const defs = compareRowDefs(input);
    const head = `<tr><th>Setting</th>${GOALS.map((g) => `<th>${GOAL_META[g].icon} ${GOAL_META[g].label}</th>`).join("")}</tr>`;
    const body = defs.map((d) => {
      if (d.group) return `<tr class="group"><td colspan="${GOALS.length + 1}">${d.group}</td></tr>`;
      const cells = tunes.map((t) => `<td class="num">${d.get(t)}</td>`).join("");
      return `<tr><td>${d.label}</td>${cells}</tr>`;
    }).join("");
    $("compareWrap").innerHTML = `<table class="compare"><thead>${head}</thead><tbody>${body}</tbody></table>`;
  }

  /* ---------- copy current tune as text ---------- */
  function tuneToText(t, input) {
    const L = [];
    L.push(`FH6 TUNE — ${GOAL_META[t.goal].label}`);
    L.push(`Car: ${input.drivetrain} ${input.engineLocation}-engine ${input.powertrain}, ${nf(t.derived.frac * 100, 0)}% front, P/W ${nf(t.derived.pw, 2)} hp/lb`);
    L.push(`— Tires: F ${nf(t.tires.front, 1)} / R ${nf(t.tires.rear, 1)} psi`);
    L.push(`— Final drive: ${nf(t.gearing.final, 2)}${t.gearing.singleSpeed ? " (single-speed)" : "  Gears: " + t.gearing.ratios.map((r) => nf(r, 2)).join(", ")}${t.gearing.topSpeed != null ? `  | Top speed ~${speedDisp(t.gearing.topSpeed)}` : ""}`);
    L.push(`— Camber: F ${nf(t.alignment.camberF, 1)} / R ${nf(t.alignment.camberR, 1)}  Toe: F ${nf(t.alignment.toeF, 1)} / R ${nf(t.alignment.toeR, 1)}  Caster: ${nf(t.alignment.caster, 1)}`);
    L.push(`— ARB: F ${t.arb.front} / R ${t.arb.rear}`);
    L.push(`— Springs: F ${springDisp(t.springs.front)} / R ${springDisp(t.springs.rear)} ${units === "metric" ? "kgf/mm" : "lb/in"}  Ride: F ${rideDisp(t.springs.rideF)} / R ${rideDisp(t.springs.rideR)} ${units === "metric" ? "cm" : "in"}`);
    L.push(`— Damping: Reb F ${nf(t.damping.reboundF, 1)} / R ${nf(t.damping.reboundR, 1)}  Bump F ${nf(t.damping.bumpF, 1)} / R ${nf(t.damping.bumpR, 1)}`);
    const af = t.aero.front == null ? "n/a" : (t.aero.frontLbf != null ? aeroDisp(t.aero.frontLbf) : t.aero.front + "%");
    const ar = t.aero.rear == null ? "n/a" : (t.aero.rearLbf != null ? aeroDisp(t.aero.rearLbf) : t.aero.rear + "%");
    L.push(`— Aero: ${!t.aero.applicable ? "none installed" : `F ${af} / R ${ar}`}`);
    L.push(`— Brakes: balance ${t.braking.balance}% front, pressure ${t.braking.pressure}%`);
    if (t.differential.driveline === "AWD")
      L.push(`— Diff: rear ${t.differential.accel}/${t.differential.decel}%, front ${t.differential.frontAccel}/${t.differential.frontDecel}%, center ${t.differential.centerRear}% rear`);
    else
      L.push(`— Diff: ${t.differential.driveline} accel ${t.differential.accel}% / decel ${t.differential.decel}%`);
    return L.join("\n");
  }

  /* ---------- state + wiring ---------- */
  let currentGoal = "Circuit";

  function buildGoalTabs() {
    $("goalTabs").innerHTML = GOALS.map((g) =>
      `<button role="tab" data-goal="${g}" class="${g === currentGoal ? "active" : ""}">${GOAL_META[g].icon} ${GOAL_META[g].label}</button>`
    ).join("");
    $("goalTabs").querySelectorAll("button").forEach((b) =>
      b.addEventListener("click", () => { currentGoal = b.dataset.goal; refresh(); }));
  }

  // show only the range fields for the wing(s) the car actually has
  function syncAeroFields() {
    const kit = $("aeroKit").value;
    const showFront = kit === "Front" || kit === "Full";
    const showRear = kit === "Rear" || kit === "Full";
    document.querySelectorAll(".aero-front-field").forEach((e) => (e.style.display = showFront ? "" : "none"));
    document.querySelectorAll(".aero-rear-field").forEach((e) => (e.style.display = showRear ? "" : "none"));
    $("aeroRanges").style.display = kit === "None" ? "none" : "";
    $("aeroHint").style.display = kit === "None" ? "none" : "";
  }

  function showErrors(errors) {
    // Surface validation errors instead of rendering a broken tune.
    $("output").hidden = false; $("summaryStrip").hidden = true;
    $("compareWrap").hidden = true;
    $("output").innerHTML =
      `<div class="card error-card"><h3><span class="ico">⚠️</span>Check your inputs</h3>` +
      `<div class="rows">` +
      errors.map((e) => `<div class="row"><span class="k">•</span><span class="v">${escapeHtml(e)}</span></div>`).join("") +
      `</div></div>`;
  }

  function refresh() {
    syncAeroFields();
    updateBiasLabel();
    const input = readInputs();
    const compare = $("compareMode").checked;
    $("goalTabs").querySelectorAll("button").forEach((b) =>
      b.classList.toggle("active", b.dataset.goal === currentGoal));
    $("goalTabs").style.opacity = compare ? 0.45 : 1;

    // Refuse to produce a tune from invalid input — show the problems instead.
    const check = validate(input);
    if (!check.valid) { showErrors(check.errors); return; }

    if (compare) {
      $("output").hidden = true; $("summaryStrip").hidden = true;
      $("compareWrap").hidden = false;
      renderCompare(input);
    } else {
      $("output").hidden = false; $("summaryStrip").hidden = false;
      $("compareWrap").hidden = true;
      renderCards(compute(input, currentGoal));
    }
  }

  function setUnits(next) {
    if (next === units) return;
    // convert the unit-bound field values so the same physical car is preserved
    Object.keys(FIELD_DIM).forEach((id) => {
      const el = $(id);
      if (el.value.trim() === "") return; // leave optional blanks blank
      const dim = FIELD_DIM[id];
      const conv = next === "metric" ? (+el.value) / M2I[dim] : (+el.value) * M2I[dim];
      // round per dimension so round-trips stay clean (spring/weight whole in imperial)
      const dp = dim === "ride" ? 1 : dim === "spring" ? (next === "metric" ? 2 : 0) : 0;
      el.value = +conv.toFixed(dp);
    });
    units = next;
    // relabel unit hints
    document.querySelectorAll("#unitToggle button").forEach((b) =>
      b.classList.toggle("active", b.dataset.units === units));
    const lbl = UNIT_LABEL[units];
    $("powerUnit").textContent = lbl.power; $("torqueUnit").textContent = lbl.torque;
    $("weightUnit").textContent = lbl.weight;
    $("rhUnit").textContent = lbl.ride; $("rhUnit2").textContent = lbl.ride;
    $("srUnit").textContent = lbl.spring; $("srUnit2").textContent = lbl.spring;
    $("afUnit").textContent = lbl.aero; $("afUnit2").textContent = lbl.aero;
    $("arUnit").textContent = lbl.aero; $("arUnit2").textContent = lbl.aero;
    $("tireUnit").textContent = lbl.ride; $("topSpeedUnit").textContent = lbl.speed;
    refresh();
  }

  function escapeHtml(s) {
    return String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
  }

  function init() {
    buildGoalTabs();
    // live updates on every input
    document.querySelectorAll("input, select").forEach((el) => {
      el.addEventListener("input", refresh);
      el.addEventListener("change", refresh);
    });
    document.querySelectorAll("#unitToggle button").forEach((b) =>
      b.addEventListener("click", () => setUnits(b.dataset.units)));
    $("compareMode").addEventListener("change", refresh);
    $("biasReset").addEventListener("click", () => { $("handlingBias").value = "0"; refresh(); });
    $("copyBtn").addEventListener("click", async () => {
      const input = readInputs();
      const text = $("compareMode").checked
        ? GOALS.map((g) => tuneToText(compute(input, g), input)).join("\n\n")
        : tuneToText(compute(input, currentGoal), input);
      try {
        await navigator.clipboard.writeText(text);
        const btn = $("copyBtn"); const old = btn.textContent;
        btn.textContent = "Copied ✓"; setTimeout(() => (btn.textContent = old), 1400);
      } catch (e) {
        // clipboard may be blocked on file://; fall back to a prompt
        window.prompt("Copy your tune:", text);
      }
    });
    refresh();
  }

  document.addEventListener("DOMContentLoaded", init);
})();
