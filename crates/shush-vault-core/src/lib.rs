use aes_gcm::aead::{Aead, KeyInit, OsRng};
use aes_gcm::{Aes256Gcm, Nonce};
use base64::engine::general_purpose::STANDARD;
use base64::Engine;
use chrono::{DateTime, Utc};
use pbkdf2::pbkdf2_hmac;
use rand::RngCore;
use serde::{Deserialize, Serialize};
use sha2::Sha256;
use thiserror::Error;
use uuid::Uuid;

const FORMAT_VERSION: u8 = 1;
const SALT_LEN: usize = 16;
const NONCE_LEN: usize = 12;
const KEY_LEN: usize = 32;
const KDF_NAME: &str = "pbkdf2-sha256";
const KDF_ITERATIONS: u32 = 310_000;
const CIPHER_NAME: &str = "aes-256-gcm";

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct SecretRecord {
    pub id: Uuid,
    pub workspace: String,
    pub name: String,
    pub value: String,
    pub environment: String,
    pub provider: String,
    pub notes: String,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
    pub deleted_at: Option<DateTime<Utc>>,
}

impl SecretRecord {
    pub fn create(workspace: &str, name: &str, value: &str, environment: &str, provider: &str, notes: &str) -> Self {
        let now = Utc::now();
        Self {
            id: Uuid::new_v4(),
            workspace: fallback(workspace, "Default"),
            name: name.trim().to_owned(),
            value: value.to_owned(),
            environment: fallback(environment, "Dev"),
            provider: provider.trim().to_owned(),
            notes: notes.trim().to_owned(),
            created_at: now,
            updated_at: now,
            deleted_at: None,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct Vault {
    pub records: Vec<SecretRecord>,
}

impl Vault {
    pub fn add(&mut self, record: SecretRecord) {
        self.records.insert(0, record);
    }

    pub fn visible_records(&self) -> impl Iterator<Item = &SecretRecord> {
        self.records.iter().filter(|record| record.deleted_at.is_none())
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct EncryptedVault {
    pub version: u8,
    pub kdf: String,
    pub iterations: u32,
    pub cipher: String,
    pub salt: String,
    pub nonce: String,
    pub ciphertext: String,
}

#[derive(Debug, Error)]
pub enum VaultError {
    #[error("invalid passphrase")]
    InvalidPassphrase,
    #[error("encryption failed")]
    EncryptionFailed,
    #[error("decryption failed")]
    DecryptionFailed,
    #[error("serialization failed: {0}")]
    Serialization(#[from] serde_json::Error),
    #[error("invalid base64: {0}")]
    Base64(#[from] base64::DecodeError),
    #[error("invalid encrypted vault payload")]
    InvalidPayload,
}

pub fn encrypt_vault(vault: &Vault, passphrase: &str) -> Result<EncryptedVault, VaultError> {
    if passphrase.is_empty() {
        return Err(VaultError::InvalidPassphrase);
    }

    let mut salt = [0_u8; SALT_LEN];
    let mut nonce = [0_u8; NONCE_LEN];
    OsRng.fill_bytes(&mut salt);
    OsRng.fill_bytes(&mut nonce);

    let key = derive_key(passphrase, &salt, KDF_ITERATIONS)?;
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|_| VaultError::EncryptionFailed)?;
    let plaintext = serde_json::to_vec(vault)?;
    let ciphertext = cipher
        .encrypt(Nonce::from_slice(&nonce), plaintext.as_ref())
        .map_err(|_| VaultError::EncryptionFailed)?;

    Ok(EncryptedVault {
        version: FORMAT_VERSION,
        kdf: KDF_NAME.to_owned(),
        iterations: KDF_ITERATIONS,
        cipher: CIPHER_NAME.to_owned(),
        salt: STANDARD.encode(salt),
        nonce: STANDARD.encode(nonce),
        ciphertext: STANDARD.encode(ciphertext),
    })
}

pub fn decrypt_vault(encrypted: &EncryptedVault, passphrase: &str) -> Result<Vault, VaultError> {
    if encrypted.version != FORMAT_VERSION || encrypted.kdf != KDF_NAME || encrypted.cipher != CIPHER_NAME {
        return Err(VaultError::InvalidPayload);
    }

    let salt = STANDARD.decode(&encrypted.salt)?;
    let nonce = STANDARD.decode(&encrypted.nonce)?;
    let ciphertext = STANDARD.decode(&encrypted.ciphertext)?;
    if salt.len() != SALT_LEN || nonce.len() != NONCE_LEN {
        return Err(VaultError::InvalidPayload);
    }

    let key = derive_key(passphrase, &salt, encrypted.iterations)?;
    let cipher = Aes256Gcm::new_from_slice(&key).map_err(|_| VaultError::DecryptionFailed)?;
    let plaintext = cipher
        .decrypt(Nonce::from_slice(&nonce), ciphertext.as_ref())
        .map_err(|_| VaultError::DecryptionFailed)?;
    Ok(serde_json::from_slice(&plaintext)?)
}

fn derive_key(passphrase: &str, salt: &[u8], iterations: u32) -> Result<[u8; KEY_LEN], VaultError> {
    if iterations == 0 {
        return Err(VaultError::InvalidPayload);
    }

    let mut key = [0_u8; KEY_LEN];
    pbkdf2_hmac::<Sha256>(passphrase.as_bytes(), salt, iterations, &mut key);
    Ok(key)
}

fn fallback(value: &str, default_value: &str) -> String {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        default_value.to_owned()
    } else {
        trimmed.to_owned()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encrypts_and_decrypts_vault_without_plaintext_leakage() {
        let mut vault = Vault::default();
        vault.add(SecretRecord::create("Demo", "OPENAI_API_KEY", "sk-secret", "Dev", "OpenAI", "notes"));

        let encrypted = encrypt_vault(&vault, "correct horse battery staple").unwrap();
        let serialized = serde_json::to_string(&encrypted).unwrap();

        assert!(!serialized.contains("OPENAI_API_KEY"));
        assert!(!serialized.contains("sk-secret"));

        let decrypted = decrypt_vault(&encrypted, "correct horse battery staple").unwrap();
        assert_eq!(decrypted.records[0].name, "OPENAI_API_KEY");
        assert_eq!(decrypted.records[0].value, "sk-secret");
    }

    #[test]
    fn rejects_wrong_passphrase() {
        let mut vault = Vault::default();
        vault.add(SecretRecord::create("Demo", "KEY", "value", "Dev", "", ""));
        let encrypted = encrypt_vault(&vault, "right").unwrap();

        assert!(matches!(
            decrypt_vault(&encrypted, "wrong"),
            Err(VaultError::DecryptionFailed)
        ));
    }
}
