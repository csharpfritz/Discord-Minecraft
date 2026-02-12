plugins {
    java
}

group = "com.discordminecraft"
version = "1.0.0"

java {
    toolchain {
        languageVersion.set(JavaLanguageVersion.of(21))
    }
}

repositories {
    mavenCentral()
    maven("https://repo.papermc.io/repository/maven-public/")
}

dependencies {
    compileOnly("io.papermc.paper:paper-api:1.21.4-R0.1-SNAPSHOT")
    implementation("redis.clients:jedis:5.2.0")
    implementation("com.google.code.gson:gson:2.11.0")
}

tasks.jar {
    from(configurations.runtimeClasspath.get().map { if (it.isDirectory) it else zipTree(it) }) {
        exclude("META-INF/*.SF", "META-INF/*.DSA", "META-INF/*.RSA")
    }
    duplicatesStrategy = DuplicatesStrategy.EXCLUDE

    doLast {
        val pluginsDir = file("${rootProject.projectDir}/../AppHost/minecraft-data/plugins")
        if (pluginsDir.exists()) {
            copy {
                from(archiveFile)
                into(pluginsDir)
            }
            println("Copied ${archiveFileName.get()} to ${pluginsDir.absolutePath}")
        }
    }
}
