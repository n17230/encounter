// Client-local "the game is live" flag: set by MainMenu when the lobby's
// phase becomes Started (the local player has, or is about to have, a
// character), reset by NetworkBootstrap when the local client stops. Until
// it's set there is no player to drive, so Escape does nothing and the
// in-game menu can't open.
public static class TestingAreaGate
{
    public static bool Entered;
}
