namespace Noted.Rendering;

/// <summary>
/// A compact table of <c>:shortcode:</c> → emoji mappings, covering the common GitHub set. Used to
/// swap <c>:rocket:</c> for 🚀 in the live view while the document keeps the plain shortcode text.
/// </summary>
public static class Emoji
{
    public static bool TryGet(string shortcode, out string emoji) => Map.TryGetValue(shortcode, out emoji!);

    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // faces & people
        ["smile"] = "😄", ["smiley"] = "😃", ["grin"] = "😁", ["laughing"] = "😆",
        ["joy"] = "😂", ["rofl"] = "🤣", ["blush"] = "😊", ["slightly_smiling_face"] = "🙂",
        ["wink"] = "😉", ["heart_eyes"] = "😍", ["kissing_heart"] = "😘", ["thinking"] = "🤔",
        ["neutral_face"] = "😐", ["expressionless"] = "😑", ["unamused"] = "😒", ["roll_eyes"] = "🙄",
        ["smirk"] = "😏", ["grimacing"] = "😬", ["relieved"] = "😌", ["pensive"] = "😔",
        ["confused"] = "😕", ["worried"] = "😟", ["cry"] = "😢", ["sob"] = "😭",
        ["frowning"] = "😦", ["anguished"] = "😧", ["fearful"] = "😨", ["weary"] = "😩",
        ["triumph"] = "😤", ["angry"] = "😠", ["rage"] = "😡", ["sunglasses"] = "😎",
        ["nerd_face"] = "🤓", ["sleeping"] = "😴", ["dizzy_face"] = "😵", ["mask"] = "😷",
        ["scream"] = "😱", ["astonished"] = "😲", ["flushed"] = "😳", ["sweat_smile"] = "😅",
        ["yum"] = "😋", ["stuck_out_tongue"] = "😛", ["upside_down_face"] = "🙃", ["shushing_face"] = "🤫",
        ["partying_face"] = "🥳", ["exploding_head"] = "🤯", ["star_struck"] = "🤩", ["pleading_face"] = "🥺",

        // hands & gestures
        ["wave"] = "👋", ["raised_hand"] = "✋", ["ok_hand"] = "👌", ["v"] = "✌️",
        ["crossed_fingers"] = "🤞", ["thumbsup"] = "👍", ["+1"] = "👍", ["thumbsdown"] = "👎",
        ["-1"] = "👎", ["fist"] = "✊", ["punch"] = "👊", ["clap"] = "👏",
        ["raised_hands"] = "🙌", ["pray"] = "🙏", ["point_up"] = "☝️", ["point_down"] = "👇",
        ["point_left"] = "👈", ["point_right"] = "👉", ["muscle"] = "💪", ["writing_hand"] = "✍️",
        ["handshake"] = "🤝",

        // hearts & symbols
        ["heart"] = "❤️", ["orange_heart"] = "🧡", ["yellow_heart"] = "💛", ["green_heart"] = "💚",
        ["blue_heart"] = "💙", ["purple_heart"] = "💜", ["black_heart"] = "🖤", ["broken_heart"] = "💔",
        ["sparkling_heart"] = "💖", ["two_hearts"] = "💕", ["100"] = "💯", ["anger"] = "💢",
        ["boom"] = "💥", ["collision"] = "💥", ["dizzy"] = "💫", ["sweat_drops"] = "💦",
        ["zzz"] = "💤", ["star"] = "⭐", ["star2"] = "🌟", ["sparkles"] = "✨",
        ["fire"] = "🔥", ["snowflake"] = "❄️", ["zap"] = "⚡", ["cloud"] = "☁️",
        ["sunny"] = "☀️", ["rainbow"] = "🌈", ["droplet"] = "💧",

        // marks
        ["white_check_mark"] = "✅", ["heavy_check_mark"] = "✔️", ["ballot_box_with_check"] = "☑️",
        ["x"] = "❌", ["negative_squared_cross_mark"] = "❎", ["heavy_multiplication_x"] = "✖️",
        ["warning"] = "⚠️", ["question"] = "❓", ["grey_question"] = "❔", ["exclamation"] = "❗",
        ["bangbang"] = "‼️", ["heavy_plus_sign"] = "➕", ["heavy_minus_sign"] = "➖",
        ["heavy_division_sign"] = "➗", ["curly_loop"] = "➰", ["white_circle"] = "⚪", ["red_circle"] = "🔴",
        ["large_blue_circle"] = "🔵", ["green_circle"] = "🟢", ["arrow_right"] = "➡️", ["arrow_left"] = "⬅️",
        ["arrow_up"] = "⬆️", ["arrow_down"] = "⬇️", ["recycle"] = "♻️", ["check"] = "✅",

        // objects & work
        ["rocket"] = "🚀", ["memo"] = "📝", ["pencil"] = "📝", ["pencil2"] = "✏️",
        ["book"] = "📖", ["books"] = "📚", ["bookmark"] = "🔖", ["page_facing_up"] = "📄",
        ["clipboard"] = "📋", ["pushpin"] = "📌", ["paperclip"] = "📎", ["scissors"] = "✂️",
        ["bulb"] = "💡", ["computer"] = "💻", ["desktop_computer"] = "🖥️", ["keyboard"] = "⌨️",
        ["floppy_disk"] = "💾", ["cd"] = "💿", ["camera"] = "📷", ["telephone"] = "☎️",
        ["email"] = "✉️", ["envelope"] = "✉️", ["inbox_tray"] = "📥", ["outbox_tray"] = "📤",
        ["package"] = "📦", ["lock"] = "🔒", ["unlock"] = "🔓", ["key"] = "🔑",
        ["mag"] = "🔍", ["link"] = "🔗", ["gear"] = "⚙️", ["wrench"] = "🔧",
        ["hammer"] = "🔨", ["bug"] = "🐛", ["calendar"] = "📅", ["clock"] = "🕐",
        ["hourglass"] = "⌛", ["watch"] = "⌚", ["money_with_wings"] = "💸", ["moneybag"] = "💰",
        ["gem"] = "💎", ["trophy"] = "🏆", ["medal"] = "🏅", ["dart"] = "🎯",
        ["chart_with_upwards_trend"] = "📈", ["chart_with_downwards_trend"] = "📉", ["bar_chart"] = "📊",

        // celebration & misc
        ["tada"] = "🎉", ["confetti_ball"] = "🎊", ["balloon"] = "🎈", ["gift"] = "🎁",
        ["crown"] = "👑", ["ghost"] = "👻", ["alien"] = "👽", ["robot"] = "🤖",
        ["skull"] = "💀", ["poop"] = "💩", ["hankey"] = "💩", ["clown_face"] = "🤡",
        ["eyes"] = "👀", ["speech_balloon"] = "💬", ["thought_balloon"] = "💭", ["bell"] = "🔔",
        ["no_bell"] = "🔕", ["loudspeaker"] = "📢", ["mega"] = "📣", ["musical_note"] = "🎵",
        ["notes"] = "🎶", ["mute"] = "🔇", ["speaker"] = "🔈", ["sound"] = "🔉",

        // nature, food, animals (a light sampling)
        ["dog"] = "🐶", ["cat"] = "🐱", ["mouse"] = "🐭", ["rabbit"] = "🐰",
        ["bear"] = "🐻", ["panda_face"] = "🐼", ["tiger"] = "🐯", ["lion"] = "🦁",
        ["cow"] = "🐮", ["pig"] = "🐷", ["frog"] = "🐸", ["monkey_face"] = "🐵",
        ["chicken"] = "🐔", ["penguin"] = "🐧", ["bird"] = "🐦", ["fish"] = "🐟",
        ["whale"] = "🐳", ["dolphin"] = "🐬", ["snake"] = "🐍", ["turtle"] = "🐢",
        ["bee"] = "🐝", ["ant"] = "🐜", ["butterfly"] = "🦋", ["seedling"] = "🌱",
        ["evergreen_tree"] = "🌲", ["palm_tree"] = "🌴", ["cactus"] = "🌵", ["four_leaf_clover"] = "🍀",
        ["maple_leaf"] = "🍁", ["mushroom"] = "🍄", ["rose"] = "🌹", ["sunflower"] = "🌻",
        ["apple"] = "🍎", ["banana"] = "🍌", ["pizza"] = "🍕", ["hamburger"] = "🍔",
        ["fries"] = "🍟", ["coffee"] = "☕", ["tea"] = "🍵", ["beer"] = "🍺",
        ["cake"] = "🍰", ["birthday"] = "🎂", ["cookie"] = "🍪", ["candy"] = "🍬",

        // flags & travel (sampling)
        ["earth_americas"] = "🌎", ["earth_africa"] = "🌍", ["earth_asia"] = "🌏", ["globe_with_meridians"] = "🌐",
        ["moon"] = "🌙", ["car"] = "🚗", ["airplane"] = "✈️", ["train"] = "🚆",
        ["ship"] = "🚢", ["anchor"] = "⚓", ["house"] = "🏠", ["office"] = "🏢",
    };
}
