#!/bin/sh
# Xcode Cloud runs this after cloning the repository. The web files (App/public) and capacitor.config.json
# are not committed (see ios/.gitignore), so they are written here by Capacitor, for the production server.
set -e

brew install node

cd "$CI_PRIMARY_REPOSITORY_PATH/GamesLane.Mobile"
npm ci
npx cap sync ios
