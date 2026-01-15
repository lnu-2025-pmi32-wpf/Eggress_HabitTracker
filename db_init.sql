CREATE TABLE users (
    user_id SERIAL PRIMARY KEY,
    login VARCHAR(64) NOT NULL UNIQUE,
    email VARCHAR(128) NOT NULL UNIQUE,
    password_hash VARCHAR(128) NOT NULL,
    theme_preference BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE habits (
    habit_id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL,
    title VARCHAR(120) NOT NULL,
    description TEXT,
    period_n INTEGER NOT NULL,
    current_points INTEGER NOT NULL DEFAULT 0,
    egg_color_hex VARCHAR(7) NOT NULL,
    last_check_in TIMESTAMP,
    is_hatched BOOLEAN NOT NULL DEFAULT FALSE,

    CONSTRAINT fk_habits_user
        FOREIGN KEY (user_id) REFERENCES users(user_id)
        ON DELETE CASCADE,

    CONSTRAINT chk_period
        CHECK (period_n IN (1,7,30)),

    CONSTRAINT chk_points
        CHECK (current_points BETWEEN 0 AND 60)
);

CREATE TABLE habit_logs (
    log_id SERIAL PRIMARY KEY,
    habit_id INTEGER NOT NULL,
    points_added INTEGER NOT NULL,
    log_timestamp TIMESTAMP NOT NULL DEFAULT NOW(),

    CONSTRAINT fk_logs_habit
        FOREIGN KEY (habit_id) REFERENCES habits(habit_id)
        ON DELETE CASCADE
);
