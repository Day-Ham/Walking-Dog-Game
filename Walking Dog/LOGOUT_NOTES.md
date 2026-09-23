# Logout flow notes

The confirmation dialog's **LOGOUT BUTTON** uses `LogoutController`.

When it is pressed, the controller:

1. disables the button to prevent duplicate requests;
2. writes `RememberMe = 0` and saves PlayerPrefs before changing scenes;
3. clears the Firebase and Google sessions (and cancels a pending Google sign-in); and
4. loads `Title Screen`.

Clearing the preference is essential: it prevents the title screen's existing automatic-login check from immediately sending a manually logged-out player back into the game. A later normal login stores the current Remember Me toggle selection again.
