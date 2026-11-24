(() => {
  function getRadioValue(name: string): string | null {
    const checked = document.querySelector<HTMLInputElement>(
      `input[name="${name}"]:checked`
    );
    return checked ? checked.value : null;
  }

  function syncModePanels(): void {
    const mode = getRadioValue("inputMode") ?? "odcs";
    document
      .querySelectorAll<HTMLElement>("[data-workbench-panel]")
      .forEach((panel) => {
        const target = panel.getAttribute("data-workbench-panel");
        panel.hidden = target !== mode && target !== "both";
      });
  }

  function deriveLabelFromPointer(pointer: string): string | null {
    const parts = pointer
      .replace(/^\$\./, "")
      .replace(/^\//, "")
      .split(/[/.]/)
      .filter(Boolean);

    if (parts.length === 0) {
      return null;
    }

    const leaf = parts[parts.length - 1].trim();
    switch (leaf.toLowerCase()) {
      case "urn":
        return "URN";
      case "ukprn":
        return "UKPRN";
      case "laestab":
        return "LAEstab";
      default: {
        const sanitized = leaf.replace(/[\s/\\]+/g, "_").replace(/\.\./g, "");
        return sanitized || null;
      }
    }
  }

  function syncIdentifierControls(): void {
    const isNone = (getRadioValue("identifierMode") ?? "").toLowerCase() === "none";
    const pointerGroup = document.getElementById("identifier-pointer-group");
    const propSelect = document.querySelector<HTMLSelectElement>(
      "[data-identifier-property]"
    );
    const pointerInput = document.getElementById(
      "identifierPointer"
    ) as HTMLInputElement | null;
    const labelInput = document.getElementById(
      "identifierLabel"
    ) as HTMLInputElement | null;
    const pointerPreview = document.getElementById("identifier-pointer-preview");
    const labelPreview = document.getElementById("identifier-label-preview");
    const requiredPreview = document.getElementById("identifier-required-preview");

    if (isNone) {
      if (pointerGroup) pointerGroup.hidden = true;
      if (pointerPreview) pointerPreview.textContent = "None (disabled)";
      if (labelPreview) labelPreview.textContent = "None";
      if (requiredPreview) {
        requiredPreview.textContent = "No (identifier partitioning disabled)";
      }
      return;
    }

    if (!propSelect) return;

    const selectedOption =
      propSelect.selectedIndex >= 0
        ? propSelect.options[propSelect.selectedIndex]
        : null;
    const selectedValue = propSelect.value || "__custom__";
    const isCustom = selectedValue === "__custom__";

    if (pointerGroup) {
      pointerGroup.hidden = !isCustom;
    }

    let activePointer = "";
    if (isCustom) {
      activePointer = pointerInput ? pointerInput.value.trim() : "";
    } else {
      activePointer = selectedValue;
      if (pointerInput) pointerInput.value = selectedValue;
    }

    const derivedLabel =
      selectedOption?.getAttribute("data-derived-label") ||
      deriveLabelFromPointer(activePointer);

    if (labelInput) {
      labelInput.placeholder = derivedLabel
        ? `Auto: ${derivedLabel}`
        : "Derived from property";
    }

    const customLabel = labelInput ? labelInput.value.trim() : "";
    if (pointerPreview) {
      pointerPreview.textContent =
        activePointer || "Not set — enter or select a property";
    }
    if (labelPreview) {
      labelPreview.textContent =
        customLabel || derivedLabel || "Derived from property name";
    }
    if (requiredPreview) {
      const reqAttr = selectedOption?.getAttribute("data-required");
      if (reqAttr === "true") {
        requiredPreview.textContent = "Yes (required by JSON Schema)";
      } else if (reqAttr === "false") {
        requiredPreview.textContent = "No (optional in JSON Schema)";
      } else {
        requiredPreview.textContent =
          "Derived automatically from JSON Schema 'required' on Verify / Save";
      }
    }
  }

  function wireFileInputs(): void {
    document
      .querySelectorAll<HTMLInputElement>("[data-load-target]")
      .forEach((fileInput) => {
        fileInput.addEventListener("change", () => {
          const file = fileInput.files?.[0];
          const targetId = fileInput.getAttribute("data-load-target");
          const target = targetId
            ? (document.getElementById(targetId) as HTMLTextAreaElement | null)
            : null;
          if (!file || !target) return;

          const fileNameInput = document.getElementById(
            "fileName"
          ) as HTMLInputElement | null;
          if (fileNameInput && !fileNameInput.value) {
            fileNameInput.value = file.name;
          }

          const reader = new FileReader();
          reader.onload = () => {
            target.value = typeof reader.result === "string" ? reader.result : "";
          };
          reader.readAsText(file);
        });
    });
  }

  function syncDefaultTabs(): void {
    if (window.location.hash) {
      return;
    }

    document
      .querySelectorAll<HTMLElement>(".govuk-tabs[data-default-tab]")
      .forEach((tabsRoot) => {
        const defaultTabId = tabsRoot.getAttribute("data-default-tab");
        if (!defaultTabId) {
          return;
        }

        const targetTab = tabsRoot.querySelector<HTMLAnchorElement>(
          `a.govuk-tabs__tab[href="#${defaultTabId}"]`
        );
        if (
          targetTab &&
          targetTab.getAttribute("role") === "tab" &&
          targetTab.getAttribute("aria-selected") !== "true"
        ) {
          targetTab.click();
        }
      });
  }

  document.addEventListener("DOMContentLoaded", () => {
    document
      .querySelectorAll<HTMLInputElement>('input[name="inputMode"]')
      .forEach((radio) => radio.addEventListener("change", syncModePanels));

    document
      .querySelectorAll<HTMLInputElement>('input[name="identifierMode"]')
      .forEach((radio) =>
        radio.addEventListener("change", syncIdentifierControls)
      );

    document
      .querySelector<HTMLSelectElement>("[data-identifier-property]")
      ?.addEventListener("change", syncIdentifierControls);

    document
      .getElementById("identifierPointer")
      ?.addEventListener("input", syncIdentifierControls);

    document
      .getElementById("identifierLabel")
      ?.addEventListener("input", syncIdentifierControls);

    syncModePanels();
    syncIdentifierControls();
    wireFileInputs();
    syncDefaultTabs();
  });
})();
