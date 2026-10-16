# MotoHub Professional Demo

Responsive multi-page motorcycle service website with a dark graphite / red visual system. Each screen has its own HTML file, CSS file, and JavaScript entry file. Shared styling and app logic live in `css/common.css`, `js/common.js`, `js/auth.js`, and `js/data.js`.

## Run locally
1. Extract the ZIP.
2. Open the `MotoHub_Professional` folder in VS Code.
3. Install/use Live Server and open `index.html`.
4. The splash screen automatically proceeds to Login.

## API reference
Start the API in a terminal:

```powershell
dotnet run --project MotoHub.Api
```

The API listens at `http://localhost:3000`; the health check is `http://localhost:3000/health`. Open [`api.html`](./api.html) with Live Server to browse the Swagger UI and try requests (an internet connection is required to load Swagger UI from its CDN).

To connect Postman:
1. Import [`Postman/MotoHub.postman_collection.json`](./Postman/MotoHub.postman_collection.json) into Postman.
2. Send **Authentication → Login as seeded admin** for admin endpoints, or **Authentication → Register rider** / **Login rider** for rider endpoints. The collection saves the returned access token automatically.
3. Send an authenticated request such as **Account → Get my profile**. The collection's Bearer Token uses its saved `accessToken`.

The default admin credentials are for local demo use only. API data is stored separately from the website demo in `%LOCALAPPDATA%\\MotoHub\\motohub.json`; set `MOTOHUB_DATA_PATH` to change the storage location. The API signs one-hour JWTs with a random process-local key by default; set `MOTOHUB_JWT_SECRET` to a stable secret (at least 32 bytes) if tokens should survive server restarts. Password reset only returns an acknowledgement and does not send email; checkout is a demo and does not process payment. This is a development backend, not production-hardened authentication or payments.

## Demo admin
- Email: `admin@motohub.com`
- Password: `Admin123!`

## Included screens
Splash, Login, Sign Up, Forgot Password, Home, Services, Parts, Cart, Booking, Booking Success, Booking Details, My Bookings, My Garage, Add Motorcycle, Profile, Notifications, Account Settings, Admin dashboard.

## Functionality
- Sign up and login with separate user/admin role routing.
- Browser-local user/session storage.
- Service selection and booking creation, detail view, cancellation, status updates.
- Motorcycle garage add/remove.
- Parts search/category, cart, demo checkout.
- Profile editing, notifications, local preferences.
- Admin booking status management, rider directory, service price editing.

## Important limitation
The website UI remains a front-end demo using browser `localStorage`, separate from the included API's local JSON store; data is not synchronized between them or across machines. The API is intended for local Postman development, not production deployment. It does not provide real email delivery or payment processing. Do not use real passwords or payment details.
