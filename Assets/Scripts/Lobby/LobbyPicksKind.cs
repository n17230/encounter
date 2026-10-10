// Which half of a player's picks a lobby confirm carries - a confirm is sent
// for the category of the screen just left (spells when leaving Skills,
// gear when leaving Equipment), never both at once.
public enum LobbyPicksKind
{
    Spells,
    Gear
}
