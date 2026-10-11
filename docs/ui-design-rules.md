# Website UI design rules

User-approved conventions, recorded October 10, 2026. Apply these throughout the website and to new features. Preserve existing text unless a request explicitly changes it. Later explicit user instructions take precedence.

## Colors and states

- Focus: use the rounded purple outline everywhere. Color `#8f78b7`, 3px outline, 3px offset. Inputs show it when focused; buttons and links show it for keyboard focus. Composite controls get one outline around the outer control, not duplicate outlines around their children.
- Persistent selection: use the Recent selection color, `#f1efe9`, with `#d8d5cc` where a selection border is needed. This applies to navigation items, folders, asset type cards, icon choices, and kit rows. Selection stays visible after the pointer leaves.
- Hover on unselected items: use the lighter Documentation color, `#f9f8f5`. Hover must not replace the selected state.
- Top module tabs are an explicit exception: the selected tab is white, `#ffffff`, joining the open white workspace. Keep the rounded top corners.

## Page titles and subtitles

Documentation is the reference for page heading typography. Inventory, Overview, Kits, Requests, Configuration, and Settings share that design.

- Title: 22px, weight 600, letter spacing -0.5px, line height 1.2; 20px at widths up to 700px.
- Subtitle: 12px, weight 400, line height 1.7, color `#888890`.
- Title to subtitle: 4px. Keep wording specific to the page.
- These rules apply to page headings, not headings inside authored documents or every card heading.

## Spacing and alignment

- Standard gap between independent cards and panels: 10px, matching the Inventory category cards. Use `--card-gap` from `frontend/src/spacing.css` for horizontal and vertical card grids, including responsive layouts. Apply this to summary cards, split panels, configuration tiles, and account panels. Avoid extra child margins that increase the gap.
- Keep the explicitly approved 6px heading/content gaps and zero-gap kit/navigation rows; card spacing does not replace spacing inside forms, card content, or document text.

- All Inventory sections start their first cards or panels at the same vertical position at the same viewport size.
- Page content starts with 28px top and 30px horizontal padding on desktop; 20px top and 16px horizontal padding at widths up to 650px.
- Use a 6px layout gap from the page title/subtitle block to the first cards or panels.
- On the Inventory asset page, also use 6px below the category cards to the asset section header, and 6px from that header to the asset table.
- Do not add extra subtitle margins that enlarge these gaps. Account for parent flex/grid gaps when aligning sections.
- Keep tables and their header backgrounds spanning their panel width. Align column labels with the corresponding row text.
- The Overview Needs attention panel fills the remaining available vertical space.

## Scrolling and fixed controls

- When content exceeds the space available in a panel, make its information or options area scrollable inside that panel. Do not clip content or let it push fixed controls out of reach.
- Keep the panel header in place. Search bars and other persistent controls also stay in place; in Your kits, the header and bottom search bar stay fixed while the kit options scroll.
- Keep table column headings visible while rows scroll. Use a constrained flex/grid layout with `min-height:0` and `overflow:auto` on the content region; headers and search/footer controls do not shrink.
- Scrollbars use an 8px track with a rounded neutral thumb and 2px inset, consistently at the content region’s right edge. Padded card content must not push its scrollbar inward.
- Table column headings, including filter buttons when present, belong to the fixed header; put vertical scrolling on the rows below them.
- Scrollbars have no arrow buttons anywhere. Use the shared `frontend/src/scrollbars.css` style. The scrollbar must start below the fixed header and remain inside the scrollable content region.
- Scrollbars appear only when needed, use the same slim neutral style, and stay clear of rounded corners. Preserve wheel, touch, and keyboard scrolling.
- Apply this convention to existing panels when updating them and to new lists, previews, and option menus. For small screens, give stacked panels a practical maximum height so their content can still scroll.
- Kit preview keeps its title and status header fixed while its details scroll.

## Controls and menus

- Use clean outline icons from the existing Lucide icon set; asset icons come from the predetermined picker. Do not introduce a different icon style.
- Dropdowns use the shared custom control and menu style, including the plain chevron and rounded, neutral borders. Avoid native blue option selection and boxed arrow buttons.
- Field scope starts empty with `Select field scope`. Shared remains an available choice. Focusing it clears the displayed search text so users can type immediately without deleting Shared or an existing selection.
- Field kind uses the same dropdown design as Field scope.
- Icon picker outer box has rounded 10px corners.
- Checkboxes use the existing custom style: 16px, 4px corners, neutral dark checked fill and white checkmark, with the standard purple keyboard focus outline.
- Active type is vertically aligned with Cancel and Save type in the same footer row, with the checkbox on the left and actions on the right.
- Context menus follow the Documentation Library menu: compact spacing, rounded corners, clean outline icons, and neutral surfaces.

## Kits

- Kit choices behave and look like Documentation navigation buttons: rounded pill rows, persistent selected fill, and lighter hover on unselected rows.
- Adjacent kit rows have no vertical gap. Keep the full row clickable and keyboard accessible.
- Preserve the full-width table header and aligned columns, including Kit name aligned with the kit names.

## Navigation and future sections

- Configuration lives above Settings in the Inventory rail. Asset types, custom fields, departments, and related company directories belong there.
- Vault and Remote Support currently use empty placeholders matching Tickets, with their own titles and icons. Do not add functionality or populated dashboards without a request.
- Implement the remaining inventory enhancements one at a time and wait for the user's review before moving to the next enhancement.

## Implementation references and checks

- Card and panel spacing: `frontend/src/spacing.css`.
- Focus rules: `frontend/src/focus.css`.
- Selection rules and top-tab exception: `frontend/src/selection.css`.
- Shared page typography and spacing: `frontend/src/suite.css`.
- Documentation visual reference: `frontend/src/documentation.css`.
- Existing checks: `frontend/tests/module-appearance.spec.ts`, `frontend/tests/inventory-panel-alignment.spec.ts`, plus focused configuration and kit tests.

When changing UI, check selected, hovered, focused, and idle states; verify page heading and first-panel alignment; preserve responsive layouts and keyboard access. Prefer shared rules and components so new pages inherit these conventions. Do not claim that every existing control already conforms without inspecting it.

## Asset detail view

Asset information appears in the left panel and recorded history in the right panel. Headers stay fixed while each panel scrolls. Show dated assignment, location, status/repair-state, and other asset changes from the audit log; load older events in pages. Never invent historical assignments or repairs when no event was recorded. On small screens, stack the panels.
`nHistory receives the larger share of the asset detail dialog. Modal dialogs close when their backdrop is clicked, using the existing cancellation and busy-state behavior. Asset details use an outlined Close button aligned with the title.
History entries show the actor profile display name with the date and change. Demo events remain explicitly labeled Demo data. Vertically center all inventory row cells, including rows with wrapped names or locations.
Create an asset-type documentation folder and live page only after its first asset exists. Choosing an icon or creating the type alone must not provision documentation. Preserve existing documentation when the last asset is removed.
Entering the Inventory module opens Overview (or Inventory for accounts without Overview access). Refresh preserves the current module and Inventory section in tab-local navigation storage scoped to the signed-in user and company.
Reports uses a parameter builder for assets, people, locations and departments. Directory-based reports include records without assets; filters apply to counted assets. Results keep fixed column headers, centered row contents and selectable columns, and export the displayed rows and columns to CSV.
Onboarding is a request type under Requests, not a separate navigation tab. Requests can filter All, Equipment, or Onboarding; onboarding requests retain start dates, locations, asset assignment and handover. Onboarding report links open the matching request.

Shared workspace bottom gutter: 10px, with no additional bottom padding on full-height Inventory, Reports, Kits, Requests, Overview, and Tickets pages. Ticket list and detail panel headers use the same 54px height and vertically centered content.
