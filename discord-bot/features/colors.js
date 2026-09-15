// Shared embed color palette. BRAND is the default for static/thematic
// panels (rules, FAQ, showcases); the other three flag live events by
// severity so a glance at the color says what kind of thing happened.
module.exports = {
  BRAND: 0x8b0000, // dark blood red — neutral/thematic (rules, FAQ, Steam showcases, panels)
  SUCCESS: 0x57f287, // green — positive confirmation (ticket created, giveaway posted)
  WARNING: 0xfee75c, // yellow — a flag worth a look (report, edit, fresh account)
  DANGER: 0xed4245, // red — active/negative event (deletion, raid alert, punishment)
};
