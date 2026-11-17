(function () {
  "use strict";

  var SCALAR_TYPES = new Set([
    "string",
    "integer",
    "number",
    "date",
    "timestamp",
    "decimal",
    "float",
    "double",
    "long",
    "int"
  ]);

  function deriveTypeFromPointerOrNull(pointer) {
    if (!pointer || typeof pointer !== "string") return null;
    var parts = pointer
      .replace(/^\$\./, "")
      .replace(/^\//, "")
      .split(/[/.]/)
      .filter(Boolean);
    if (parts.length === 0) return null;

    var leaf = parts[parts.length - 1].trim();
    var lower = leaf.toLowerCase();
    if (lower === "urn") return "URN";
    if (lower === "ukprn") return "UKPRN";
    if (lower === "laestab") return "LAEstab";

    var sanitized = leaf.replace(/[\s/\\]+/g, "_").replace(/\.\./g, "");
    return sanitized || null;
  }

  function resolveLocalRef(root, refPointer) {
    if (!refPointer || typeof refPointer !== "string" || !refPointer.startsWith("#/")) {
      return null;
    }
    var segments = refPointer.slice(2).split("/").filter(Boolean);
    var current = root;
    for (var i = 0; i < segments.length; i++) {
      var seg = segments[i].replace(/~1/g, "/").replace(/~0/g, "~");
      if (!current || typeof current !== "object" || !(seg in current)) {
        return null;
      }
      current = current[seg];
    }
    return current && typeof current === "object" ? current : null;
  }

  function mergeSchemaNode(node, root, visitedRefs, depth) {
    if (!node || typeof node !== "object" || depth > 10) return node || {};
    var mergedProps = {};
    var mergedReq = new Set();
    var resolvedType = node.type;

    function absorb(src, includeReq) {
      if (!src || typeof src !== "object") return;
      if (src.type && !resolvedType) resolvedType = src.type;
      if (src.properties && typeof src.properties === "object") {
        Object.keys(src.properties).forEach(function (k) {
          mergedProps[k] = src.properties[k];
        });
      }
      if (includeReq && Array.isArray(src.required)) {
        src.required.forEach(function (r) {
          if (typeof r === "string") mergedReq.add(r);
        });
      }
    }

    if (typeof node.$ref === "string" && !visitedRefs.has(node.$ref)) {
      var nextVisited = new Set(visitedRefs);
      nextVisited.add(node.$ref);
      var refTarget = resolveLocalRef(root, node.$ref);
      if (refTarget) {
        absorb(mergeSchemaNode(refTarget, root, nextVisited, depth + 1), true);
      }
    }

    if (Array.isArray(node.allOf)) {
      node.allOf.forEach(function (branch) {
        absorb(mergeSchemaNode(branch, root, visitedRefs, depth + 1), true);
      });
    }

    ["anyOf", "oneOf"].forEach(function (comb) {
      if (Array.isArray(node[comb])) {
        node[comb].forEach(function (branch) {
          absorb(mergeSchemaNode(branch, root, visitedRefs, depth + 1), false);
        });
      }
    });

    absorb(node, true);

    var out = Object.assign({}, node);
    if (resolvedType) out.type = resolvedType;
    if (Object.keys(mergedProps).length > 0) out.properties = mergedProps;
    if (mergedReq.size > 0) out.required = Array.from(mergedReq);
    return out;
  }

  function extractJsonTypes(schemaNode) {
    if (!schemaNode || typeof schemaNode !== "object") return [];
    var types = new Set();

    function addType(t) {
      if (typeof t === "string") types.add(t.toLowerCase());
      else if (Array.isArray(t)) t.forEach(function (x) { if (typeof x === "string") types.add(x.toLowerCase()); });
    }

    addType(schemaNode.type);
    ["allOf", "anyOf", "oneOf"].forEach(function (comb) {
      if (Array.isArray(schemaNode[comb])) {
        schemaNode[comb].forEach(function (b) {
          extractJsonTypes(b).forEach(function (t) { types.add(t); });
        });
      }
    });
    if (schemaNode.properties) types.add("object");
    if (schemaNode.items) types.add("array");
    if (types.size === 0 && Array.isArray(schemaNode.enum)) types.add("string");
    return Array.from(types);
  }

  function discoverJsonSchemaProperties(rawJson) {
    if (!rawJson || !rawJson.trim()) return [];
    var root;
    try {
      root = JSON.parse(rawJson);
    } catch (_) {
      return [];
    }
    if (!root || typeof root !== "object") return [];

    var results = [];
    var seen = new Set();

    function walk(node, segments, parentRequired, visitedRefs, depth) {
      if (!node || typeof node !== "object" || depth > 10) return;
      var effective = mergeSchemaNode(node, root, visitedRefs, depth);
      if (!effective.properties || typeof effective.properties !== "object") return;

      var reqSet = new Set(Array.isArray(effective.required) ? effective.required : []);
      Object.keys(effective.properties).forEach(function (propName) {
        var childRaw = effective.properties[propName];
        if (!childRaw || typeof childRaw !== "object") return;

        var childEff = mergeSchemaNode(childRaw, root, visitedRefs, depth + 1);
        var childReq = parentRequired && reqSet.has(propName);
        var nextSegments = segments.concat([propName]);
        var types = extractJsonTypes(childEff);

        if (childEff.properties && typeof childEff.properties === "object") {
          walk(childEff, nextSegments, childReq, visitedRefs, depth + 1);
          return;
        }

        if (types.includes("array")) {
          return;
        }

        var scalarList = types.filter(function (t) { return SCALAR_TYPES.has(t); });
        if (scalarList.length === 0) return;

        var allowsNull = types.includes("null");
        var pointer = "/" + nextSegments.map(function (s) { return s.replace(/~/g, "~0").replace(/\//g, "~1"); }).join("/");
        if (seen.has(pointer.toLowerCase())) return;
        seen.add(pointer.toLowerCase());

        results.push({
          pointer: pointer,
          name: propName,
          valueType: scalarList.join("|"),
          required: childReq && !allowsNull,
          derivedType: deriveTypeFromPointerOrNull(pointer) || propName
        });
      });
    }

    walk(root, [], true, new Set(), 0);
    return results;
  }

  function unquoteYaml(raw) {
    var s = raw.trim();
    if ((s.startsWith('"') && s.endsWith('"')) || (s.startsWith("'") && s.endsWith("'"))) {
      return s.slice(1, -1).trim();
    }
    return s;
  }

  function discoverOdcsProperties(rawYaml) {
    if (!rawYaml || !rawYaml.trim()) return [];
    var lines = rawYaml.replace(/\r\n/g, "\n").split("\n");
    var inSchema = false;
    var schemaIndent = -1;
    var inRootHeader = false;
    var rootPropsIndent = -1;
    var nodes = [];
    var activeIdx = -1;

    for (var i = 0; i < lines.length; i++) {
      var rawLine = lines[i];
      var trimmed = rawLine.trim();
      if (!trimmed || trimmed.startsWith("#")) continue;

      var indent = rawLine.length - rawLine.replace(/^ +/, "").length;
      if (!inSchema) {
        if (trimmed === "schema:" || trimmed.startsWith("schema:")) {
          inSchema = true;
          schemaIndent = indent;
        }
        continue;
      }

      if (indent <= schemaIndent && !trimmed.startsWith("-")) {
        break;
      }

      if (trimmed.startsWith("- name:")) {
        var nameVal = unquoteYaml(trimmed.slice(7));
        if (rootPropsIndent < 0 && indent <= schemaIndent + 4) {
          inRootHeader = true;
          activeIdx = -1;
        } else {
          inRootHeader = false;
          nodes.push({ indent: indent, name: nameVal, logicalType: null, required: false, hasProperties: false });
          activeIdx = nodes.length - 1;
        }
        continue;
      }

      if (inRootHeader) {
        if (trimmed.startsWith("properties:")) {
          rootPropsIndent = indent;
          inRootHeader = false;
        }
        continue;
      }

      if (activeIdx >= 0) {
        var cur = nodes[activeIdx];
        if (trimmed.startsWith("logicalType:")) {
          cur.logicalType = unquoteYaml(trimmed.slice(12));
        } else if (trimmed.startsWith("required:")) {
          cur.required = unquoteYaml(trimmed.slice(9)).toLowerCase() === "true";
        } else if (trimmed.startsWith("properties:")) {
          cur.hasProperties = true;
        }
      }
    }

    var results = [];
    var seen = new Set();
    var stack = [];

    for (var j = 0; j < nodes.length; j++) {
      var n = nodes[j];
      while (stack.length > 0 && stack[stack.length - 1].indent >= n.indent) {
        stack.pop();
      }

      var parentIsArray = stack.some(function (s) { return s.isArray; });
      var ltype = (n.logicalType || "").toLowerCase();
      var isArray = ltype === "array";
      var isObj = n.hasProperties || ltype === "object";
      var hasChildren = j + 1 < nodes.length && nodes[j + 1].indent > n.indent;

      stack.push({ indent: n.indent, name: n.name, required: n.required, isArray: isArray });
      if (parentIsArray || isArray || isObj || hasChildren) continue;

      var valType = n.logicalType || "string";
      if (!SCALAR_TYPES.has(valType.toLowerCase())) continue;

      var pointer = "/" + stack.map(function (s) { return s.name.replace(/~/g, "~0").replace(/\//g, "~1"); }).join("/");
      if (seen.has(pointer.toLowerCase())) continue;
      seen.add(pointer.toLowerCase());

      results.push({
        pointer: pointer,
        name: n.name,
        valueType: valType,
        required: stack.every(function (s) { return s.required; }),
        derivedType: deriveTypeFromPointerOrNull(pointer) || n.name
      });
    }

    return results;
  }

  function syncModePanels() {
    var selected = document.querySelector('input[name="inputMode"]:checked');
    var mode = selected ? selected.value : "odcs";

    document.querySelectorAll("[data-workbench-panel]").forEach(function (panel) {
      var target = panel.getAttribute("data-workbench-panel");
      var visible = target === mode || target === "both";
      panel.hidden = !visible;
    });
  }

  function syncIdentifierControls() {
    var modeRadio = document.querySelector('input[name="identifierMode"]:checked');
    var isNone = modeRadio && modeRadio.value.toLowerCase() === "none";

    var configPanel = document.getElementById("identifier-config-panel");
    var pointerGroup = document.getElementById("identifier-pointer-group");
    var propSelect = document.querySelector("[data-identifier-property]");
    var pointerInput = document.getElementById("identifierPointer");
    var labelInput = document.getElementById("identifierLabel");
    var pointerPreview = document.getElementById("identifier-pointer-preview");
    var labelPreview = document.getElementById("identifier-label-preview");
    var requiredPreview = document.getElementById("identifier-required-preview");

    if (configPanel) {
      configPanel.hidden = Boolean(isNone);
    }

    if (isNone) {
      if (pointerGroup) pointerGroup.hidden = true;
      if (pointerPreview) pointerPreview.textContent = "None (disabled)";
      if (labelPreview) labelPreview.textContent = "None";
      if (requiredPreview) requiredPreview.textContent = "No (identifier partitioning disabled)";
      return;
    }

    if (!propSelect) return;

    var selectedOption = propSelect.options[propSelect.selectedIndex];
    var selectedValue = propSelect.value || "__custom__";
    var isCustom = selectedValue === "__custom__";

    if (pointerGroup) {
      pointerGroup.hidden = !isCustom;
    }

    var activePointer = "";
    if (isCustom) {
      activePointer = pointerInput ? pointerInput.value.trim() : "";
    } else {
      activePointer = selectedValue;
      if (pointerInput) pointerInput.value = selectedValue;
    }

    var derivedLabel = (selectedOption && selectedOption.getAttribute("data-derived-label"))
      ? selectedOption.getAttribute("data-derived-label")
      : deriveTypeFromPointerOrNull(activePointer);

    if (labelInput) {
      labelInput.placeholder = derivedLabel ? "Auto: " + derivedLabel : "Derived from property";
    }

    var customLabel = labelInput ? labelInput.value.trim() : "";
    if (pointerPreview) {
      pointerPreview.textContent = activePointer
        ? activePointer
        : "Not set — enter or select a property";
    }

    if (labelPreview) {
      labelPreview.textContent = customLabel || derivedLabel || "Derived from property name";
    }

    if (requiredPreview) {
      var reqAttr = selectedOption ? selectedOption.getAttribute("data-required") : null;
      if (reqAttr === "true") {
        requiredPreview.textContent = "Yes (required by JSON Schema)";
      } else if (reqAttr === "false") {
        requiredPreview.textContent = "No (optional in JSON Schema)";
      } else {
        requiredPreview.textContent = "Derived automatically from JSON Schema 'required' on Verify / Save";
      }
    }
  }

  function refreshIdentifierPropertyOptions() {
    var propSelect = document.querySelector("[data-identifier-property]");
    var schemaTextarea = document.getElementById("schemaJson");
    var odcsTextarea = document.getElementById("odcsYaml");
    if (!propSelect || (!schemaTextarea && !odcsTextarea)) return;

    var selectedMode = document.querySelector('input[name="inputMode"]:checked');
    var mode = selectedMode ? selectedMode.value : "odcs";
    var discovered = mode === "json"
      ? discoverJsonSchemaProperties(schemaTextarea ? schemaTextarea.value : "")
      : discoverOdcsProperties(odcsTextarea ? odcsTextarea.value : "");

    var currentSelection = propSelect.value || "__custom__";

    propSelect.innerHTML = "";

    var customOpt = document.createElement("option");
    customOpt.value = "__custom__";
    customOpt.textContent = "Enter JSON Pointer manually";
    customOpt.setAttribute("data-custom", "true");
    propSelect.appendChild(customOpt);

    discovered.forEach(function (item) {
      var opt = document.createElement("option");
      opt.value = item.pointer;
      opt.textContent = item.pointer + " (" + item.valueType + ", " + (item.required ? "required" : "optional") + ")";
      opt.setAttribute("data-pointer", item.pointer);
      opt.setAttribute("data-derived-label", item.derivedType);
      opt.setAttribute("data-required", item.required ? "true" : "false");
      propSelect.appendChild(opt);
    });

    if (currentSelection !== "__custom__") {
      var match = Array.from(propSelect.options).find(function (o) {
        return o.value.toLowerCase() === currentSelection.toLowerCase();
      });
      propSelect.value = match ? match.value : "__custom__";
    } else {
      propSelect.value = "__custom__";
    }

    syncIdentifierControls();
  }

  function wireFileInputs() {
    document.querySelectorAll("[data-load-target]").forEach(function (fileInput) {
      fileInput.addEventListener("change", function (event) {
        var file = event.target.files && event.target.files[0];
        if (!file) return;

        var targetId = fileInput.getAttribute("data-load-target");
        var target = targetId ? document.getElementById(targetId) : null;
        if (!target) return;

        var fileNameInput = document.getElementById("fileName");
        if (fileNameInput && !fileNameInput.value) {
          fileNameInput.value = file.name;
        }

        var reader = new FileReader();
        reader.onload = function (loadEvent) {
          target.value = String(loadEvent.target && loadEvent.target.result ? loadEvent.target.result : "");
          refreshIdentifierPropertyOptions();
        };
        reader.readAsText(file);
      });
    });
  }

  function renderVerifyErrorSummary(container, titleText, messageText) {
    var summary = document.createElement("div");
    summary.className = "govuk-error-summary";
    summary.setAttribute("role", "alert");

    var heading = document.createElement("h2");
    heading.className = "govuk-error-summary__title";
    heading.textContent = titleText;

    var body = document.createElement("div");
    body.className = "govuk-error-summary__body";

    var paragraph = document.createElement("p");
    paragraph.className = "govuk-body";
    paragraph.textContent = messageText;

    body.appendChild(paragraph);
    summary.appendChild(heading);
    summary.appendChild(body);
    container.replaceChildren(summary);
  }

  function wireProgressiveVerify() {
    var verifyBtn = document.querySelector("[data-verify-button]");
    var form = document.getElementById("workbench-form");
    var resultContainer = document.getElementById("workbench-verify-result");
    if (!verifyBtn || !form || !resultContainer) return;

    verifyBtn.addEventListener("click", async function (event) {
      event.preventDefault();
      syncIdentifierControls();
      verifyBtn.disabled = true;
      var originalText = verifyBtn.textContent;
      verifyBtn.textContent = "Verifying...";

      try {
        var response = await fetch("/manage/workbench/verify", {
          method: "POST",
          body: new FormData(form),
          headers: { "X-Requested-With": "fetch" }
        });

        var contentType = (response.headers.get("content-type") || "").toLowerCase();
        var bodyText = await response.text();
        if (contentType.indexOf("text/html") !== -1) {
          resultContainer.innerHTML = bodyText;
        } else {
          renderVerifyErrorSummary(
            resultContainer,
            "Verification request failed (HTTP " + response.status + ")",
            bodyText || "Unexpected non-HTML response from verification endpoint."
          );
        }
        resultContainer.scrollIntoView({ behavior: "smooth", block: "nearest" });
      } catch (err) {
        renderVerifyErrorSummary(
          resultContainer,
          "Verification request failed",
          String(err)
        );
      } finally {
        verifyBtn.disabled = false;
        verifyBtn.textContent = originalText;
      }
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll('input[name="inputMode"]').forEach(function (radio) {
      radio.addEventListener("change", function () {
        syncModePanels();
        refreshIdentifierPropertyOptions();
      });
    });

    document.querySelectorAll('input[name="identifierMode"]').forEach(function (radio) {
      radio.addEventListener("change", syncIdentifierControls);
    });

    var propSelect = document.querySelector("[data-identifier-property]");
    if (propSelect) {
      propSelect.addEventListener("change", syncIdentifierControls);
    }

    var pointerInput = document.getElementById("identifierPointer");
    if (pointerInput) {
      pointerInput.addEventListener("input", syncIdentifierControls);
    }

    var labelInput = document.getElementById("identifierLabel");
    if (labelInput) {
      labelInput.addEventListener("input", syncIdentifierControls);
    }

    var schemaTextarea = document.getElementById("schemaJson");
    var odcsTextarea = document.getElementById("odcsYaml");
    [schemaTextarea, odcsTextarea].forEach(function (ta) {
      if (ta) {
        ta.addEventListener("input", refreshIdentifierPropertyOptions);
        ta.addEventListener("change", refreshIdentifierPropertyOptions);
      }
    });

    syncModePanels();
    if (schemaTextarea || odcsTextarea) {
      refreshIdentifierPropertyOptions();
    } else {
      syncIdentifierControls();
    }
    wireFileInputs();
    wireProgressiveVerify();
  });
})();
